using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Parkir.Pos;

public partial class MainWindow : Window
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CheckoutQueue _checkoutQueue = new();
    private readonly DispatcherTimer _queueTimer;
    private QuoteResponse? _currentQuote;
    private bool _isSyncingQueue;
    private bool _checkoutPending;
    private string? _photoOutPath;

    public MainWindow(PosSession session)
    {
        InitializeComponent();
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
        Title = $"Parkir | Pos Keluar - {session.FullName}";
        _queueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _queueTimer.Tick += async (_, _) => await SyncPendingCheckoutsAsync();
        _queueTimer.Start();
        UpdateConnectionLabel();
        _ = SyncPendingCheckoutsAsync();
        TicketInput.Focus();
    }

    private async void TicketInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await LoadQuoteAsync();
        }
    }

    private async void Quote_Click(object sender, RoutedEventArgs e) => await LoadQuoteAsync();

    private async Task LoadQuoteAsync()
    {
        var ticketId = TicketInput.Text.Trim();
        if (!HasApiKey())
        {
            MessageLabel.Text = "Atur environment variable PARKIR_API_KEY sebelum menggunakan POS.";
            return;
        }

        if (ticketId.Length == 0)
        {
            MessageLabel.Text = "Nomor tiket wajib diisi.";
            return;
        }

        SetBusy(true);
        try
        {
            _currentQuote = await Client.GetFromJsonAsync<QuoteResponse>(
                $"api/transactions/{Uri.EscapeDataString(ticketId)}/quote", JsonOptions);
            if (_currentQuote is null)
            {
                throw new InvalidOperationException("Server tidak mengembalikan data tiket.");
            }

            TicketLabel.Text = _currentQuote.TicketId;
            DurationLabel.Text = $"{_currentQuote.DurationMinutes} menit";
            MemberLabel.Text = _currentQuote.IsMember ? "Member aktif" : "Reguler";
            await LoadPhotoAsync(EntryPhotoImage, _currentQuote.PhotoInPath);
            ExitPhotoImage.Source = null;
            _photoOutPath = null;
            CapturePhotoButton.IsEnabled = true;
            UpdateDisplayedFee();
            MessageLabel.Text = _currentQuote.IsMember
                ? "Member aktif, tidak dikenakan tarif harian."
                : "Verifikasi kendaraan dan lanjutkan pembayaran.";
            CheckoutButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            _currentQuote = null;
            CheckoutButton.IsEnabled = false;
            MessageLabel.Text = await GetErrorMessageAsync(exception);
        }
        finally
        {
            SetBusy(false);
            TicketInput.Focus();
            TicketInput.SelectAll();
        }
    }

    private async void Checkout_Click(object sender, RoutedEventArgs e)
    {
        if (_currentQuote is null)
        {
            return;
        }
        if (_checkoutPending)
        {
            MessageLabel.Text = "Checkout ini masih menunggu konfirmasi server.";
            return;
        }

        var idempotencyKey = Guid.NewGuid();
        var request = new CheckoutRequest(
            GateId: "GATE-OUT-01",
            PaymentMethod: ((ComboBoxItem)PaymentMethod.SelectedItem).Content.ToString() ?? "CASH",
            LostTicket: LostTicketCheck.IsChecked == true,
            IdentityVerified: IdentityVerifiedCheck.IsChecked == true,
            PhotoOutPath: _photoOutPath,
            IdempotencyKey: idempotencyKey);
        var pending = new PendingCheckout(
            idempotencyKey,
            _currentQuote.TicketId,
            JsonSerializer.Serialize(request, JsonOptions),
            DateTimeOffset.UtcNow);

        SetBusy(true);
        try
        {
            _checkoutQueue.Enqueue(pending);
            _checkoutPending = true;
            using var response = await SendCheckoutAsync(pending);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode >= 500)
                {
                    MessageLabel.Text = $"Checkout {idempotencyKey:D} masih tersimpan dan menunggu sinkronisasi. Jangan buka palang sampai server mengonfirmasi.";
                    return;
                }

                _checkoutQueue.Remove(idempotencyKey);
                _checkoutPending = false;
                throw new InvalidOperationException(await ReadApiErrorAsync(response));
            }

            var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>(JsonOptions)
                ?? throw new InvalidOperationException("Server tidak mengembalikan hasil checkout.");
            _checkoutQueue.Remove(idempotencyKey);
            _checkoutPending = false;
            _currentQuote = null;
            var deviceStatus = await CompleteExitHardwareAsync(result);
            MessageLabel.Text = $"Pembayaran {result.TotalFee.ToString("C0", new System.Globalization.CultureInfo("id-ID"))} selesai. {deviceStatus}";
        }
        catch (HttpRequestException exception)
        {
            MessageLabel.Text = $"Checkout {idempotencyKey:D} tersimpan dan menunggu sinkronisasi. Jangan buka palang sampai server mengonfirmasi. {exception.Message}";
        }
        catch (Exception exception)
        {
            MessageLabel.Text = await GetErrorMessageAsync(exception);
        }
        finally
        {
            SetBusy(false);
            UpdateConnectionLabel();
        }
    }

    private async void CapturePhoto_Click(object sender, RoutedEventArgs e)
    {
        if (_currentQuote is null)
        {
            return;
        }

        SetBusy(true);
        try
        {
            byte[] imageBytes;
            string contentType;
            var cameraUrl = Environment.GetEnvironmentVariable("PARKIR_OUT_CAMERA_URL");
            if (!string.IsNullOrWhiteSpace(cameraUrl))
            {
                using var cameraClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var cameraResponse = await cameraClient.GetAsync(cameraUrl);
                cameraResponse.EnsureSuccessStatusCode();
                contentType = cameraResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                imageBytes = await cameraResponse.Content.ReadAsByteArrayAsync();
            }
            else
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Pilih foto kendaraan keluar",
                    Filter = "Gambar|*.jpg;*.jpeg;*.png;*.webp",
                    CheckFileExists = true
                };
                if (dialog.ShowDialog(this) != true)
                {
                    return;
                }

                imageBytes = await File.ReadAllBytesAsync(dialog.FileName);
                contentType = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    _ => throw new InvalidOperationException("Format foto tidak didukung.")
                };
            }

            if (imageBytes.Length == 0 || imageBytes.Length > 5 * 1024 * 1024)
            {
                throw new InvalidOperationException("Ukuran foto harus antara 1 byte dan 5 MB.");
            }

            using var content = new ByteArrayContent(imageBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            using var response = await Client.PostAsync("api/media/exits", content);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await ReadApiErrorAsync(response));
            }

            var result = await response.Content.ReadFromJsonAsync<PhotoUploadResponse>(JsonOptions)
                ?? throw new InvalidOperationException("Server tidak mengembalikan path foto.");
            _photoOutPath = result.Path;
            await LoadPhotoAsync(ExitPhotoImage, _photoOutPath);
            MessageLabel.Text = "Foto keluar tersimpan dan akan dicatat saat checkout.";
        }
        catch (Exception exception)
        {
            MessageLabel.Text = await GetErrorMessageAsync(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadPhotoAsync(System.Windows.Controls.Image target, string? relativePath)
    {
        target.Source = null;
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return;
        }

        try
        {
            var bytes = await Client.GetByteArrayAsync(
                $"api/media/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}");
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            target.Source = bitmap;
        }
        catch (HttpRequestException)
        {
            MessageLabel.Text = "Foto masuk tidak dapat diunduh dari server.";
        }
    }

    private void LostTicketCheck_Changed(object sender, RoutedEventArgs e)
    {
        var lostTicket = LostTicketCheck.IsChecked == true;
        if (IdentityVerifiedCheck is not null)
        {
            IdentityVerifiedCheck.IsEnabled = lostTicket;
            if (!lostTicket)
            {
                IdentityVerifiedCheck.IsChecked = false;
            }
        }

        UpdateDisplayedFee();
    }

    private void NewTicket_Click(object sender, RoutedEventArgs e)
    {
        _currentQuote = null;
        _checkoutPending = false;
        TicketInput.Clear();
        TicketLabel.Text = "Belum ada tiket";
        DurationLabel.Text = "-";
        MemberLabel.Text = "-";
        FeeLabel.Text = "Rp 0";
        MessageLabel.Text = "Scan tiket untuk memulai.";
        CheckoutButton.IsEnabled = false;
        CapturePhotoButton.IsEnabled = false;
        EntryPhotoImage.Source = null;
        ExitPhotoImage.Source = null;
        _photoOutPath = null;
        LostTicketCheck.IsChecked = false;
        TicketInput.Focus();
    }

    private void SetBusy(bool busy)
    {
        TicketInput.IsEnabled = !busy;
        CheckoutButton.IsEnabled = !busy && _currentQuote is not null && !_checkoutPending;
        if (busy)
        {
            ConnectionLabel.Text = "Menghubungkan...";
        }
        else
        {
            UpdateConnectionLabel();
        }
    }

    private async Task SyncPendingCheckoutsAsync()
    {
        if (_isSyncingQueue || !HasApiKey())
        {
            UpdateConnectionLabel();
            return;
        }

        _isSyncingQueue = true;
        try
        {
            foreach (var pending in _checkoutQueue.GetPending())
            {
                using var response = await SendCheckoutAsync(pending);
                if (!response.IsSuccessStatusCode)
                {
                    break;
                }

                var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>(JsonOptions);
                if (result is null)
                {
                    break;
                }

                _checkoutQueue.Remove(pending.IdempotencyKey);
                if (_currentQuote?.TicketId == pending.TicketId)
                {
                    _checkoutPending = false;
                    _currentQuote = null;
                    CheckoutButton.IsEnabled = false;
                }
                var deviceStatus = await CompleteExitHardwareAsync(result);
                MessageLabel.Text = $"Checkout {pending.TicketId} dikonfirmasi server. {deviceStatus}";
            }
        }
        catch (HttpRequestException)
        {
        }
        finally
        {
            _isSyncingQueue = false;
            UpdateConnectionLabel();
        }
    }

    private static async Task<string> CompleteExitHardwareAsync(CheckoutResponse result)
    {
        var statuses = new List<string>();
        var printerHost = Environment.GetEnvironmentVariable("PARKIR_RECEIPT_HOST");
        if (string.IsNullOrWhiteSpace(printerHost))
        {
            statuses.Add("Printer belum dikonfigurasi.");
        }
        else
        {
            try
            {
                var printerPort = int.TryParse(Environment.GetEnvironmentVariable("PARKIR_RECEIPT_PORT"), out var configuredPort)
                    ? configuredPort
                    : 9100;
                await PrintReceiptAsync(printerHost, printerPort, result);
                statuses.Add("Struk tercetak.");
            }
            catch (Exception exception) when (
                exception is IOException or SocketException or ArgumentOutOfRangeException or OperationCanceledException)
            {
                statuses.Add("Cetak struk gagal; cek printer.");
            }
        }

        var relayPortName = Environment.GetEnvironmentVariable("PARKIR_RELAY_PORT");
        var relayCommand = Environment.GetEnvironmentVariable("PARKIR_RELAY_OPEN_HEX");
        if (string.IsNullOrWhiteSpace(relayPortName) || string.IsNullOrWhiteSpace(relayCommand))
        {
            statuses.Add("Palang tidak dikonfigurasi; lakukan pembukaan manual.");
        }
        else
        {
            try
            {
                var baudRate = int.TryParse(Environment.GetEnvironmentVariable("PARKIR_RELAY_BAUD"), out var configuredBaudRate)
                    ? configuredBaudRate
                    : 9600;
                await Task.Run(() => SendRelayCommand(relayPortName, baudRate, relayCommand));
                statuses.Add("Perintah buka palang terkirim.");
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or
                    InvalidOperationException or TimeoutException)
            {
                statuses.Add("Relay gagal; lakukan pembukaan manual.");
            }
        }

        return string.Join(" ", statuses);
    }

    private static async Task PrintReceiptAsync(string host, int port, CheckoutResponse result)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await client.ConnectAsync(host, port, timeout.Token);
        await using var stream = client.GetStream();
        using var receipt = new MemoryStream();
        receipt.Write(new byte[] { 0x1B, 0x40, 0x1B, 0x61, 0x01, 0x1B, 0x45, 0x01 });
        WriteReceiptText(receipt, "PARKIR - STRUK\n");
        receipt.Write(new byte[] { 0x1B, 0x45, 0x00, 0x1B, 0x61, 0x00 });
        WriteReceiptText(receipt, "------------------------------\n");
        WriteReceiptText(receipt, $"Tiket   : {result.TicketId}\n");
        WriteReceiptText(receipt, $"Durasi  : {result.DurationMinutes} menit\n");
        WriteReceiptText(receipt, $"Status  : {result.PaymentStatus}\n");
        WriteReceiptText(receipt, $"Keluar  : {result.TimeOut.LocalDateTime:yyyy-MM-dd HH:mm:ss}\n");
        WriteReceiptText(receipt, $"Denda   : Rp {result.PenaltyFee:N0}\n");
        receipt.Write(new byte[] { 0x1B, 0x45, 0x01 });
        WriteReceiptText(receipt, $"TOTAL   : Rp {result.TotalFee:N0}\n");
        receipt.Write(new byte[] { 0x1B, 0x45, 0x00 });
        WriteReceiptText(receipt, "Terima kasih\n\n\n");
        receipt.Write(new byte[] { 0x1D, 0x56, 0x41, 0x10 });
        await stream.WriteAsync(receipt.ToArray(), timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }

    private static void WriteReceiptText(Stream destination, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        destination.Write(bytes, 0, bytes.Length);
    }

    private static void SendRelayCommand(string portName, int baudRate, string commandHex)
    {
        if (baudRate is < 300 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(baudRate));
        }

        var byteTokens = commandHex.Split(new[] { ' ', ',', ':', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (byteTokens.Length == 0)
        {
            throw new ArgumentException("Relay command kosong.", nameof(commandHex));
        }

        var command = byteTokens.Select(token => byte.Parse(token, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        using var serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            WriteTimeout = 1000,
            DtrEnable = false,
            RtsEnable = false
        };
        serialPort.Open();
        serialPort.Write(command, 0, command.Length);
        serialPort.BaseStream.Flush();
    }

    private static Task<HttpResponseMessage> SendCheckoutAsync(PendingCheckout pending)
    {
        var content = new StringContent(pending.Payload, System.Text.Encoding.UTF8, "application/json");
        return Client.PostAsync(
            $"api/transactions/{Uri.EscapeDataString(pending.TicketId)}/checkout", content);
    }

    private static HttpClient CreateClient()
    {
        var address = Environment.GetEnvironmentVariable("PARKIR_API_URL") ?? "http://127.0.0.1:5080/";
        var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
        var apiKey = Environment.GetEnvironmentVariable("PARKIR_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        }

        return client;
    }

    private static bool HasApiKey() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PARKIR_API_KEY"));

    private void UpdateConnectionLabel()
    {
        if (!HasApiKey())
        {
            ConnectionLabel.Text = "PARKIR_API_KEY belum diatur";
            return;
        }

        var pendingCount = _checkoutQueue.GetPending().Count;
        ConnectionLabel.Text = pendingCount == 0
            ? "API LAN siap"
            : $"{pendingCount} checkout menunggu sinkronisasi";
    }

    private void UpdateDisplayedFee()
    {
        if (_currentQuote is null)
        {
            return;
        }

        var amount = LostTicketCheck.IsChecked == true
            ? _currentQuote.LostTicketTotal
            : _currentQuote.TotalFee;
        FeeLabel.Text = amount.ToString("C0", new System.Globalization.CultureInfo("id-ID"));
    }

    private static async Task<string> GetErrorMessageAsync(Exception exception) =>
        exception is HttpRequestException
            ? "API tidak dapat dijangkau. Periksa server LAN dan alamat API."
            : exception.Message;

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                return error.GetString() ?? $"Checkout gagal ({(int)response.StatusCode}).";
            }
        }
        catch (JsonException)
        {
        }

        return $"Checkout gagal ({(int)response.StatusCode}).";
    }

    private sealed record QuoteResponse(
        string TicketId,
        DateTimeOffset TimeIn,
        int DurationMinutes,
        decimal TotalFee,
        decimal LostTicketTotal,
        string? PhotoInPath,
        bool IsMember,
        string PaymentStatus);

    private sealed record CheckoutRequest(
        string GateId,
        string PaymentMethod,
        bool LostTicket,
        bool IdentityVerified,
        string? PhotoOutPath,
        Guid IdempotencyKey);

    private sealed record CheckoutResponse(
        string TicketId,
        int DurationMinutes,
        decimal TotalFee,
        decimal PenaltyFee,
        string PaymentStatus,
        DateTimeOffset TimeOut);

    private sealed record PhotoUploadResponse(string Path);
}