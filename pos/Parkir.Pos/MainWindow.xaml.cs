using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Parkir.Pos;

public partial class MainWindow : Window
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CheckoutQueue _checkoutQueue = new();
    private readonly DispatcherTimer _queueTimer;
    private readonly DispatcherTimer _heartbeatTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly ObservableCollection<RecentTransaction> _recentTransactions = new();
    private readonly List<VehicleTypeDto> _vehicleTypes = [];
    private PosSession _session;
    private QuoteResponse? _currentQuote;
    private long? _motorTypeId;
    private long? _mobilTypeId;
    private long? _trukTypeId;
    private long? _selectedVehicleTypeId;
    private long? _originalVehicleTypeId;
    private string? _originalVehicleTypeName;
    private string _selectedPaymentMethod = "CASH";
    private bool _isSyncingQueue;
    private bool _isHeartbeatInProgress;
    private bool _checkoutPending;
    private bool _checkoutRequestInFlight;
    private bool _isBusy;
    private bool _isSettingClass;
    private bool _sessionRefreshInProgress;
    private bool _sessionExpiredNotified;
    private DateTimeOffset _lastSessionPromptAt = DateTimeOffset.MinValue;
    private string? _photoOutPath;

    public MainWindow(PosSession session)
    {
        InitializeComponent();
        _session = session;
        ApplySessionIdentity(session);
        RecentTransactionsList.ItemsSource = _recentTransactions;
        RecentEmptyLabel.Visibility = Visibility.Visible;

        _isSettingClass = true;
        ClassMobilButton.IsChecked = true;
        _isSettingClass = false;
        CashMethodButton.IsChecked = true;
        UpdatePlateBadge();
        UpdateCashChange();
        UpdateCheckoutState();

        _queueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _queueTimer.Tick += async (_, _) => await SyncPendingCheckoutsAsync();
        _queueTimer.Start();
        _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _heartbeatTimer.Tick += async (_, _) => await SendDeviceHeartbeatAsync();
        _heartbeatTimer.Start();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
        Closed += (_, _) =>
        {
            _queueTimer.Stop();
            _heartbeatTimer.Stop();
            _clockTimer.Stop();
        };

        UpdateConnectionLabel();
        UpdateQueueIssuesButton();
        _ = SyncPendingCheckoutsAsync();
        _ = SendDeviceHeartbeatAsync();
        _ = LoadVehicleTypesAsync();
        PlateInput.Focus();
    }

    private void ApplySessionIdentity(PosSession session)
    {
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
        Title = $"Parkir | Pos Keluar - {session.FullName}";
        OperatorLabel.Text = $"Operator: {session.FullName}";
    }

    private bool IsSessionExpired => DateTimeOffset.UtcNow >= _session.ExpiresAt;

    private bool EnsureSession(bool force)
    {
        if (!force && _session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return true;
        }
        if (_sessionRefreshInProgress)
        {
            return !IsSessionExpired;
        }

        _sessionRefreshInProgress = true;
        _lastSessionPromptAt = DateTimeOffset.UtcNow;
        try
        {
            var login = new LoginWindow(_session.Username) { Owner = this };
            if (login.ShowDialog() == true && login.Session is not null)
            {
                _session = login.Session;
                ApplySessionIdentity(_session);
                _sessionExpiredNotified = false;
                MessageLabel.Text = "Sesi kasir diperbarui.";
                return true;
            }

            return !IsSessionExpired;
        }
        finally
        {
            _sessionRefreshInProgress = false;
            UpdateCheckoutState();
            UpdateConnectionLabel();
        }
    }

    private void UpdateSessionState()
    {
        var now = DateTimeOffset.UtcNow;
        if (now >= _session.ExpiresAt)
        {
            if (!_sessionExpiredNotified)
            {
                _sessionExpiredNotified = true;
                MessageLabel.Text = "Sesi kasir berakhir. Login ulang sebelum memproses pembayaran.";
                UpdateCheckoutState();
            }

            if (!_sessionRefreshInProgress && now - _lastSessionPromptAt > TimeSpan.FromMinutes(2))
            {
                _ = EnsureSession(force: true);
            }

            return;
        }

        _sessionExpiredNotified = false;
        if (_session.ExpiresAt <= now.AddMinutes(5) &&
            !_sessionRefreshInProgress &&
            now - _lastSessionPromptAt > TimeSpan.FromMinutes(2))
        {
            _ = EnsureSession(force: false);
        }
    }

    private async void TicketInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await LoadQuoteAsync();
        }
    }

    private void PlateInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (NormalizePlate(PlateInput.Text).Length == 0)
        {
            MessageLabel.Text = "Plat nomor wajib diisi.";
            return;
        }

        TicketInput.Focus();
        TicketInput.SelectAll();
        MessageLabel.Text = "Plat tersimpan. Scan tiket atau masukkan nomor tiket.";
    }

    private async void Quote_Click(object sender, RoutedEventArgs e) => await LoadQuoteAsync();

    private async Task LoadQuoteAsync(long? vehicleTypeId = null, bool allowReauth = true)
    {
        var ticketId = TicketInput.Text.Trim();
        if (!HasApiKey())
        {
            MessageLabel.Text = "Atur environment variable PARKIR_DEVICE_KEY sebelum menggunakan POS.";
            return;
        }

        if (ticketId.Length == 0)
        {
            MessageLabel.Text = "Nomor tiket wajib diisi.";
            return;
        }

        SetBusy(true);
        var classOverride = vehicleTypeId is not null;
        try
        {
            var url = $"api/transactions/{Uri.EscapeDataString(ticketId)}/quote";
            if (vehicleTypeId is { } selectedTypeId)
            {
                url += $"?vehicleTypeId={selectedTypeId}";
            }

            _currentQuote = await Client.GetFromJsonAsync<QuoteResponse>(url, JsonOptions);
            if (_currentQuote is null)
            {
                throw new InvalidOperationException("Server tidak mengembalikan data tiket.");
            }

            if (!classOverride)
            {
                _originalVehicleTypeId = _currentQuote.VehicleTypeId;
                _originalVehicleTypeName = _currentQuote.VehicleTypeName;
                OverrideReasonInput.Clear();
                LostTicketCheck.IsChecked = false;
                IdentityNumberInput.Clear();
                IdentityVerifiedCheck.IsChecked = false;
                ExitPhotoImage.Source = null;
                ExitPhotoStatus.Text = "Belum ada foto";
                _photoOutPath = null;
            }

            _selectedVehicleTypeId = _currentQuote.VehicleTypeId;
            LostTicketCheck.IsEnabled = true;
            UpdateDetailFromQuote(_currentQuote);
            SetActiveClassFromQuote();
            UpdateOverrideState();
            await LoadPhotoAsync(EntryPhotoImage, _currentQuote.PhotoInPath);
            EntryPhotoStatus.Text = string.IsNullOrWhiteSpace(_currentQuote.PhotoInPath)
                ? "Tidak ada foto masuk"
                : "Foto masuk tersedia";
            CapturePhotoButton.IsEnabled = true;
            UpdateDisplayedFee();
            if (LostTicketCheck.IsChecked == true)
            {
                CashReceivedInput.Text = Math.Ceiling(_currentQuote.LostTicketTotal)
                    .ToString("0", CultureInfo.InvariantCulture);
            }
            UpdateCheckoutState();
            MessageLabel.Text = IsClassOverridden()
                ? $"Kelas diubah dari {_originalVehicleTypeName}. Isi alasan perubahan sebelum membayar."
                : _currentQuote.IsMember
                    ? "Member aktif, tidak dikenakan tarif harian."
                    : "Tiket ditemukan. Verifikasi kendaraan dan lanjutkan pembayaran.";
        }
        catch (HttpRequestException exception) when (
            allowReauth && exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            _currentQuote = null;
            _selectedVehicleTypeId = null;
            CheckoutButton.IsEnabled = false;
            UpdateOverrideState();
            if (EnsureSession(force: true))
            {
                await LoadQuoteAsync(vehicleTypeId, allowReauth: false);
                return;
            }

            MessageLabel.Text = "Sesi kasir berakhir. Login ulang untuk mengambil data tiket.";
        }
        catch (Exception exception)
        {
            _currentQuote = null;
            _selectedVehicleTypeId = null;
            CheckoutButton.IsEnabled = false;
            UpdateOverrideState();
            MessageLabel.Text = await GetErrorMessageAsync(exception);
        }
        finally
        {
            SetBusy(false);
            if (classOverride)
            {
                if (IsClassOverridden())
                {
                    OverrideReasonInput.Focus();
                }
                else
                {
                    CashReceivedInput.Focus();
                    CashReceivedInput.SelectAll();
                }
            }
            else
            {
                TicketInput.Focus();
                TicketInput.SelectAll();
            }
        }
    }

    private void UpdateDetailFromQuote(QuoteResponse quote)
    {
        TicketLabel.Text = quote.TicketId;
        DetailPlateText.Text = PlateInput.Text.Trim().Length > 0 ? PlateInput.Text.Trim() : "-";
        DetailClassText.Text = IsClassOverridden()
            ? $"{quote.VehicleTypeName} (dari {_originalVehicleTypeName})"
            : quote.VehicleTypeName;
        DetailTimeInText.Text = quote.TimeIn.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        DetailTimeOutText.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        DurationLabel.Text = $"{quote.DurationMinutes} menit";
        MemberLabel.Text = quote.IsMember ? "Member aktif" : "Reguler";
    }

    private void SetActiveClassFromQuote()
    {
        if (_currentQuote is null)
        {
            return;
        }

        _isSettingClass = true;
        try
        {
            var typeId = _currentQuote.VehicleTypeId;
            var typeName = _currentQuote.VehicleTypeName;
            ClassMotorButton.IsChecked = typeId == _motorTypeId ||
                (_motorTypeId is null && typeName.Contains("motor", StringComparison.OrdinalIgnoreCase));
            ClassMobilButton.IsChecked = typeId == _mobilTypeId ||
                (_mobilTypeId is null && typeName.Contains("mobil", StringComparison.OrdinalIgnoreCase));
            ClassTrukButton.IsChecked = typeId == _trukTypeId ||
                (_trukTypeId is null && (typeName.Contains("truk", StringComparison.OrdinalIgnoreCase) ||
                    typeName.Contains("bus", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            _isSettingClass = false;
        }

        ApplyClassAvailability();
    }

    private void ApplyClassAvailability()
    {
        var canSelect = !_isBusy && _currentQuote is not null;
        ClassMotorButton.IsEnabled = canSelect && _motorTypeId.HasValue;
        ClassMobilButton.IsEnabled = canSelect && _mobilTypeId.HasValue;
        ClassTrukButton.IsEnabled = canSelect && _trukTypeId.HasValue;
        ClassMotorButton.ToolTip = _motorTypeId.HasValue ? null : "Kelas Motor belum tersedia di master tarif.";
        ClassMobilButton.ToolTip = _mobilTypeId.HasValue ? null : "Kelas Mobil belum tersedia di master tarif.";
        ClassTrukButton.ToolTip = _trukTypeId.HasValue ? null : "Kelas Truk/Bus belum tersedia di master tarif.";
    }

    private bool IsClassOverridden() =>
        _currentQuote is not null &&
        _originalVehicleTypeId is { } original &&
        _selectedVehicleTypeId is { } selected &&
        selected != original;

    private void UpdateOverrideState()
    {
        var overridden = IsClassOverridden();
        OverridePanel.Visibility = overridden ? Visibility.Visible : Visibility.Collapsed;
        if (!overridden)
        {
            OverrideReasonInput.Clear();
        }

        if (_currentQuote is not null)
        {
            UpdateDetailFromQuote(_currentQuote);
            MessageLabel.Text = overridden
                ? $"Kelas diubah dari {_originalVehicleTypeName}. Isi alasan perubahan sebelum membayar."
                : "Tiket ditemukan. Verifikasi kendaraan dan lanjutkan pembayaran.";
        }

        UpdateCheckoutState();
    }

    private void OverrideReasonInput_TextChanged(object sender, TextChangedEventArgs e) => UpdateCheckoutState();

    private async Task LoadVehicleTypesAsync(bool notifyUser = false)
    {
        try
        {
            var types = await Client.GetFromJsonAsync<List<VehicleTypeDto>>("api/vehicle-types", JsonOptions);
            if (types is null)
            {
                return;
            }

            _vehicleTypes.Clear();
            _vehicleTypes.AddRange(types);
            _motorTypeId = FindVehicleTypeId("motor");
            _mobilTypeId = FindVehicleTypeId("mobil");
            _trukTypeId = FindVehicleTypeId("truk", "truck", "bus");
            SetActiveClassFromQuote();
            if (notifyUser)
            {
                MessageLabel.Text = $"{_vehicleTypes.Count} kelas tarif dimuat dari server.";
            }
        }
        catch (Exception)
        {
            MessageLabel.Text = "Daftar kelas tarif tidak dapat dimuat dari server. Periksa koneksi lalu muat ulang.";
        }
    }

    private async void ReloadVehicleTypes_Click(object sender, RoutedEventArgs e) =>
        await LoadVehicleTypesAsync(notifyUser: true);

    private long? FindVehicleTypeId(params string[] names) =>
        _vehicleTypes.FirstOrDefault(type =>
            names.Any(name => type.TypeName.Contains(name, StringComparison.OrdinalIgnoreCase)))?.Id;

    private async void VehicleClass_Checked(object sender, RoutedEventArgs e)
    {
        if (_isSettingClass || !IsLoaded || _currentQuote is null || _isBusy)
        {
            return;
        }

        var className = (sender as RadioButton)?.Tag as string;
        long? typeId = className switch
        {
            "Motor" => _motorTypeId,
            "Mobil" => _mobilTypeId,
            "Truk" => _trukTypeId,
            _ => null
        };
        if (typeId is null)
        {
            MessageLabel.Text = "Kelas tarif belum tersedia di server.";
            SetActiveClassFromQuote();
            return;
        }

        await LoadQuoteAsync(typeId);
    }

    private void PaymentMethod_Checked(object sender, RoutedEventArgs e)
    {
        if (CashPanel is null)
        {
            return;
        }

        _selectedPaymentMethod = (sender as RadioButton)?.Tag as string ?? "CASH";
        CashPanel.Visibility = _selectedPaymentMethod == "CASH" ? Visibility.Visible : Visibility.Collapsed;
        if (_currentQuote is not null)
        {
            MessageLabel.Text = $"Metode pembayaran: {_selectedPaymentMethod}";
        }
        UpdateCheckoutState();
    }

    private void CashReceived_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ChangeLabel is null)
        {
            return;
        }
        UpdateCashChange();
        UpdateCheckoutState();
    }

    private void CashReceived_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Checkout_Click(this, new RoutedEventArgs());
        }
    }

    private void PlateInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (PlateBadgeText is null)
        {
            return;
        }
        UpdatePlateBadge();
        DetailPlateText.Text = PlateInput.Text.Trim().Length > 0 ? PlateInput.Text.Trim() : "-";
        UpdateCheckoutState();
    }

    private void UpdatePlateBadge()
    {
        var plate = NormalizePlate(PlateInput.Text);
        var hasPlate = plate.Length > 0;
        PlateBadgeText.Text = hasPlate ? "TERISI" : "WAJIB";
        PlateBadgeText.Foreground = (Brush)FindResource(hasPlate ? "Accent" : "Red");
        if (PlateBadgeBorder is not null)
        {
            PlateBadgeBorder.Background = (Brush)FindResource(hasPlate ? "GreenSoft" : "WarnSoft");
        }
    }

    private void IdentityNumberInput_TextChanged(object sender, TextChangedEventArgs e) => UpdateCheckoutState();

    private void IdentityVerifiedCheck_Changed(object sender, RoutedEventArgs e) => UpdateCheckoutState();

    private void LostTicketCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (LostFieldsPanel is null)
        {
            return;
        }

        var lost = LostTicketCheck.IsChecked == true;
        LostFieldsPanel.Visibility = lost ? Visibility.Visible : Visibility.Collapsed;
        LostStatusRow.Visibility = lost ? Visibility.Visible : Visibility.Collapsed;
        IdentityNumberInput.IsEnabled = lost;
        IdentityVerifiedCheck.IsEnabled = lost;
        if (!lost)
        {
            IdentityNumberInput.Clear();
            IdentityVerifiedCheck.IsChecked = false;
        }
        else if (_currentQuote is not null)
        {
            CashReceivedInput.Text = Math.Ceiling(_currentQuote.LostTicketTotal).ToString("0", CultureInfo.InvariantCulture);
        }

        UpdateDisplayedFee();
        UpdateCheckoutState();
    }

    private async void Checkout_Click(object sender, RoutedEventArgs e)
    {
        if (_currentQuote is null)
        {
            return;
        }
        if (!EnsureSession(force: IsSessionExpired))
        {
            MessageLabel.Text = "Sesi kasir berakhir. Login ulang sebelum memproses pembayaran.";
            return;
        }
        if (_checkoutPending)
        {
            MessageLabel.Text = "Checkout ini masih menunggu konfirmasi server.";
            return;
        }
        if (!IsCheckoutReady())
        {
            MessageLabel.Text = GetValidationMessage();
            return;
        }

        var quote = _currentQuote;
        var plate = NormalizePlate(PlateInput.Text);
        var lostTicket = LostTicketCheck.IsChecked == true;
        var idempotencyKey = Guid.NewGuid();
        var gateId = Environment.GetEnvironmentVariable("PARKIR_GATE_ID")?.Trim().ToUpperInvariant() ?? "GATE-OUT-01";
        var request = new CheckoutRequest(
            GateId: gateId,
            PaymentMethod: _selectedPaymentMethod,
            LostTicket: lostTicket,
            IdentityVerified: IdentityVerifiedCheck.IsChecked == true,
            PhotoOutPath: _photoOutPath,
            IdempotencyKey: idempotencyKey,
            VehicleTypeId: _selectedVehicleTypeId ?? quote.VehicleTypeId,
            LicensePlate: plate,
            OverrideReason: IsClassOverridden() ? OverrideReasonInput.Text.Trim() : null,
            ExpectedTotalFee: GetCurrentTotal());
        var pending = new PendingCheckout(
            idempotencyKey,
            quote.TicketId,
            JsonSerializer.Serialize(request, JsonOptions),
            DateTimeOffset.UtcNow,
            "PENDING",
            0,
            null,
            null,
            false);

        SetBusy(true);
        _checkoutRequestInFlight = true;
        try
        {
            _checkoutQueue.Enqueue(pending);
            _checkoutPending = true;
            var outcome = await ProcessCheckoutAsync(pending);
            if (outcome.Kind == CheckoutOutcomeKind.Retry)
            {
                MessageLabel.Text = $"Checkout {idempotencyKey:D} masih tersimpan dan menunggu sinkronisasi. " +
                    "Jangan buka palang sampai server mengonfirmasi.";
            }
            else if (outcome.Kind == CheckoutOutcomeKind.Failed)
            {
                _checkoutPending = false;
                MessageLabel.Text = $"Checkout ditolak server: {outcome.Error}";
            }
        }
        catch (HttpRequestException exception)
        {
            MessageLabel.Text = $"Checkout {idempotencyKey:D} tersimpan dan menunggu sinkronisasi. " +
                $"Jangan buka palang sampai server mengonfirmasi. {exception.Message}";
        }
        catch (Exception exception)
        {
            MessageLabel.Text = await GetErrorMessageAsync(exception);
        }
        finally
        {
            _checkoutRequestInFlight = false;
            SetBusy(false);
            UpdateConnectionLabel();
            UpdateQueueIssuesButton();
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
            ExitPhotoStatus.Text = "Foto keluar tersimpan";
            MessageLabel.Text = "Foto keluar tersimpan dan akan dicatat saat checkout.";
            UpdateCheckoutState();
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
            MessageLabel.Text = "Foto tidak dapat diunduh dari server.";
        }
    }

    private void NewTicket_Click(object sender, RoutedEventArgs e)
    {
        if (_checkoutPending)
        {
            var answer = MessageBox.Show(
                this,
                "Checkout tiket ini belum dikonfirmasi server. Jangan buka palang sebelum konfirmasi. " +
                "Lanjut membuat tiket baru?",
                "Checkout belum terkonfirmasi",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        ResetTicketFields("Scan tiket untuk memulai.");
    }

    private void ResetTicketFields(string message)
    {
        _currentQuote = null;
        _selectedVehicleTypeId = null;
        _originalVehicleTypeId = null;
        _originalVehicleTypeName = null;
        _photoOutPath = null;
        _checkoutPending = false;
        PlateInput.Clear();
        TicketInput.Clear();
        TicketLabel.Text = "Belum ada tiket";
        DetailPlateText.Text = "-";
        DetailClassText.Text = "-";
        DetailTimeInText.Text = "-";
        DetailTimeOutText.Text = "-";
        DurationLabel.Text = "-";
        MemberLabel.Text = "-";
        FeeLabel.Text = "Rp 0";
        EntryPhotoImage.Source = null;
        ExitPhotoImage.Source = null;
        EntryPhotoStatus.Text = "Belum ada foto";
        ExitPhotoStatus.Text = "Belum ada foto";
        CapturePhotoButton.IsEnabled = false;
        LostTicketCheck.IsEnabled = false;
        LostTicketCheck.IsChecked = false;
        LostFieldsPanel.Visibility = Visibility.Collapsed;
        LostStatusRow.Visibility = Visibility.Collapsed;
        IdentityNumberInput.Clear();
        IdentityVerifiedCheck.IsChecked = false;
        CashMethodButton.IsChecked = true;
        CashReceivedInput.Text = "20000";
        _isSettingClass = true;
        ClassMotorButton.IsChecked = false;
        ClassMobilButton.IsChecked = false;
        ClassTrukButton.IsChecked = false;
        _isSettingClass = false;
        ApplyClassAvailability();
        OverridePanel.Visibility = Visibility.Collapsed;
        OverrideReasonInput.Clear();
        UpdatePlateBadge();
        UpdateCashChange();
        UpdateCheckoutState();
        MessageLabel.Text = message;
        PlateInput.Focus();
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        PlateInput.IsEnabled = !busy;
        TicketInput.IsEnabled = !busy;
        CashReceivedInput.IsEnabled = !busy;
        CapturePhotoButton.IsEnabled = !busy && _currentQuote is not null;
        LostTicketCheck.IsEnabled = !busy && _currentQuote is not null;
        IdentityNumberInput.IsEnabled = !busy && LostTicketCheck.IsChecked == true;
        IdentityVerifiedCheck.IsEnabled = !busy && LostTicketCheck.IsChecked == true;
        OverrideReasonInput.IsEnabled = !busy && IsClassOverridden();
        ApplyClassAvailability();
        UpdateCheckoutState();
        if (busy)
        {
            ConnectionLabel.Text = "Menghubungkan...";
        }
        else
        {
            UpdateConnectionLabel();
        }
    }

    private void UpdateCheckoutState()
    {
        if (CheckoutButton is null)
        {
            return;
        }

        var ready = IsCheckoutReady();
        CheckoutButton.IsEnabled = !_isBusy && ready;
        if (CheckoutHintLabel is null)
        {
            return;
        }

        if (_isBusy || _currentQuote is null || ready)
        {
            CheckoutHintLabel.Visibility = Visibility.Collapsed;
            return;
        }

        CheckoutHintLabel.Text = GetValidationMessage();
        CheckoutHintLabel.Visibility = Visibility.Visible;
    }

    private bool IsCheckoutReady()
    {
        if (_currentQuote is null || _checkoutPending || _checkoutRequestInFlight || _isBusy || IsSessionExpired)
        {
            return false;
        }
        if (NormalizePlate(PlateInput.Text).Length == 0)
        {
            return false;
        }
        if (IsClassOverridden() && OverrideReasonInput.Text.Trim().Length < 5)
        {
            return false;
        }
        if (LostTicketCheck.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(IdentityNumberInput.Text) ||
                IdentityVerifiedCheck.IsChecked != true ||
                _photoOutPath is null)
            {
                return false;
            }
        }
        if (_selectedPaymentMethod == "CASH" &&
            ParseAmount(CashReceivedInput.Text) < GetCurrentTotal())
        {
            return false;
        }
        return true;
    }

    private string GetValidationMessage()
    {
        if (_checkoutRequestInFlight)
        {
            return "Checkout sedang diproses.";
        }
        if (_checkoutPending)
        {
            return "Checkout sedang menunggu konfirmasi server.";
        }
        if (IsSessionExpired)
        {
            return "Sesi kasir berakhir. Login ulang sebelum memproses pembayaran.";
        }
        if (NormalizePlate(PlateInput.Text).Length == 0)
        {
            return "Plat nomor wajib diisi.";
        }
        if (IsClassOverridden() && OverrideReasonInput.Text.Trim().Length < 5)
        {
            return "Alasan perubahan kelas tarif wajib diisi, minimal 5 karakter.";
        }
        if (LostTicketCheck.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(IdentityNumberInput.Text))
            {
                return "Nomor identitas wajib diisi untuk karcis hilang.";
            }
            if (IdentityVerifiedCheck.IsChecked != true)
            {
                return "Centang verifikasi identitas sebelum melanjutkan.";
            }
            if (_photoOutPath is null)
            {
                return "Foto keluar wajib diambil untuk karcis hilang.";
            }
        }
        if (_selectedPaymentMethod == "CASH" &&
            ParseAmount(CashReceivedInput.Text) < GetCurrentTotal())
        {
            return "Nominal pembayaran kurang dari total tagihan.";
        }
        return "Lengkapi data transaksi terlebih dahulu.";
    }

    private decimal GetCurrentTotal()
    {
        if (_currentQuote is null)
        {
            return 0m;
        }
        return LostTicketCheck.IsChecked == true
            ? _currentQuote.LostTicketTotal
            : _currentQuote.TotalFee;
    }

    private void UpdateDisplayedFee()
    {
        if (_currentQuote is null)
        {
            FeeLabel.Text = "Rp 0";
            UpdateCashChange();
            return;
        }

        FeeLabel.Text = GetCurrentTotal().ToString("C0", new CultureInfo("id-ID"));
        UpdateCashChange();
        UpdateCheckoutState();
    }

    private void UpdateCashChange()
    {
        var total = GetCurrentTotal();
        var paid = ParseAmount(CashReceivedInput.Text);
        var difference = paid - total;
        ChangeLabel.Text = difference >= 0
            ? difference.ToString("C0", new CultureInfo("id-ID"))
            : $"Kurang {(-difference).ToString("C0", new CultureInfo("id-ID"))}";
        ChangeLabel.Foreground = (Brush)FindResource(difference >= 0 ? "Accent" : "Red");
    }

    private void AddRecentTransaction(string? plate, string? vehicleClass, decimal total)
    {
        var labelPlate = string.IsNullOrWhiteSpace(plate) ? "-" : plate;
        var labelClass = string.IsNullOrWhiteSpace(vehicleClass) ? "Kendaraan" : vehicleClass;
        _recentTransactions.Insert(0, new RecentTransaction(
            DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
            $"{labelPlate} · {labelClass}",
            total.ToString("C0", new CultureInfo("id-ID"))));
        while (_recentTransactions.Count > 6)
        {
            _recentTransactions.RemoveAt(_recentTransactions.Count - 1);
        }
        RecentEmptyLabel.Visibility = _recentTransactions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task SyncPendingCheckoutsAsync()
    {
        if (_isSyncingQueue || _checkoutRequestInFlight || !HasApiKey())
        {
            UpdateConnectionLabel();
            return;
        }
        if (IsSessionExpired)
        {
            UpdateConnectionLabel();
            return;
        }

        _isSyncingQueue = true;
        try
        {
            foreach (var pending in _checkoutQueue.GetWorkItems(DateTimeOffset.UtcNow))
            {
                var outcome = await ProcessCheckoutAsync(pending);
                if (outcome.Kind == CheckoutOutcomeKind.Retry)
                {
                    break;
                }
            }
        }
        catch (Exception)
        {
            // Queue tetap tersimpan dan akan dicoba lagi pada timer berikutnya.
        }
        finally
        {
            _isSyncingQueue = false;
            UpdateConnectionLabel();
            UpdateQueueIssuesButton();
        }
    }

    private async Task<CheckoutOutcome> ProcessCheckoutAsync(PendingCheckout item)
    {
        if (item.Status == "CONFIRMED" && !item.HardwareDone)
        {
            return await CompleteConfirmedCheckoutAsync(item);
        }

        HttpResponseMessage response;
        try
        {
            response = await SendCheckoutAsync(item);
        }
        catch (HttpRequestException exception)
        {
            return ScheduleRetry(item, $"API tidak dapat dijangkau: {exception.Message}");
        }
        catch (TaskCanceledException)
        {
            return ScheduleRetry(item, "Waktu koneksi ke API habis.");
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            if (!EnsureSession(force: true))
            {
                return ScheduleRetry(item, "Sesi kasir berakhir; login ulang diperlukan.", TimeSpan.FromSeconds(10));
            }

            try
            {
                response = await SendCheckoutAsync(item);
            }
            catch (HttpRequestException exception)
            {
                return ScheduleRetry(item, $"API tidak dapat dijangkau: {exception.Message}");
            }
            catch (TaskCanceledException)
            {
                return ScheduleRetry(item, "Waktu koneksi ke API habis.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                return ScheduleRetry(item, "Server menolak sesi kasir; login ulang diperlukan.", TimeSpan.FromSeconds(10));
            }
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>(JsonOptions);
                if (result is null)
                {
                    return ScheduleRetry(item, "Server tidak mengembalikan hasil checkout yang valid.");
                }

                var resultPayload = JsonSerializer.Serialize(result, JsonOptions);
                _checkoutQueue.MarkConfirmed(item.IdempotencyKey, resultPayload);
                return await CompleteConfirmedCheckoutAsync(item with
                {
                    Status = "CONFIRMED",
                    ResultPayload = resultPayload
                });
            }

            var error = await ReadApiErrorAsync(response);
            if ((int)response.StatusCode >= 500 ||
                response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
            {
                return ScheduleRetry(item, error);
            }

            _checkoutQueue.MarkFailed(item.IdempotencyKey, $"HTTP {(int)response.StatusCode}: {error}");
            return new CheckoutOutcome(CheckoutOutcomeKind.Failed, Error: error);
        }
    }

    private CheckoutOutcome ScheduleRetry(PendingCheckout item, string error, TimeSpan? delay = null)
    {
        var seconds = delay?.TotalSeconds ??
            Math.Min(300, 5 * Math.Pow(2, Math.Min(item.AttemptCount, 6)));
        _checkoutQueue.MarkRetry(item.IdempotencyKey, error, TimeSpan.FromSeconds(seconds));
        return new CheckoutOutcome(CheckoutOutcomeKind.Retry, Error: error);
    }

    private async Task<CheckoutOutcome> CompleteConfirmedCheckoutAsync(PendingCheckout item)
    {
        CheckoutResponse? result = null;
        CheckoutRequest? request = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(item.ResultPayload))
            {
                result = JsonSerializer.Deserialize<CheckoutResponse>(item.ResultPayload, JsonOptions);
            }
            request = JsonSerializer.Deserialize<CheckoutRequest>(item.Payload, JsonOptions);
        }
        catch (JsonException)
        {
        }

        if (result is null)
        {
            _checkoutQueue.MarkFailed(item.IdempotencyKey, "Hasil checkout tersimpan tidak dapat dibaca.");
            return new CheckoutOutcome(CheckoutOutcomeKind.Failed,
                Error: "Hasil checkout tersimpan tidak dapat dibaca.");
        }

        var plate = request?.LicensePlate;
        var vehicleClass = request?.VehicleTypeId is { } typeId
            ? _vehicleTypes.FirstOrDefault(type => type.Id == typeId)?.TypeName
            : null;
        AddRecentTransaction(plate, vehicleClass, result.TotalFee);
        var deviceStatus = await CompleteExitHardwareAsync(result, plate, vehicleClass);
        _checkoutQueue.MarkHardwareDone(item.IdempotencyKey);

        if (_currentQuote?.TicketId == item.TicketId)
        {
            _checkoutPending = false;
            ResetTicketFields(
                $"Pembayaran {result.TotalFee.ToString("C0", new CultureInfo("id-ID"))} selesai. {deviceStatus}");
        }
        else
        {
            MessageLabel.Text = $"Checkout {item.TicketId} dikonfirmasi server. {deviceStatus}";
        }

        return new CheckoutOutcome(CheckoutOutcomeKind.Success, result);
    }

    private async Task SendDeviceHeartbeatAsync()
    {
        if (_isHeartbeatInProgress || !HasApiKey() || IsSessionExpired)
        {
            return;
        }

        _isHeartbeatInProgress = true;
        try
        {
            var request = new DeviceHeartbeatRequest(
                PosSettings.DeviceId,
                "CASHIER_POS",
                Environment.GetEnvironmentVariable("PARKIR_DEVICE_NAME") ?? $"Pos - {_session.FullName}",
                Environment.GetEnvironmentVariable("PARKIR_APP_VERSION") ?? "1.0.0");
            using var response = await Client.PostAsJsonAsync("api/devices/heartbeat", request, JsonOptions);
            if (response.IsSuccessStatusCode)
            {
                UpdateConnectionLabel();
            }
        }
        catch (Exception)
        {
            // Heartbeat berikutnya akan mencoba lagi.
        }
        finally
        {
            _isHeartbeatInProgress = false;
        }
    }

    private static async Task<string> CompleteExitHardwareAsync(
        CheckoutResponse result,
        string? plate = null,
        string? vehicleClass = null)
    {
        var statuses = new List<string>();
        var gateCommandId = Guid.NewGuid();
        var gateId = PosSettings.DeviceId;
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
                await PrintReceiptAsync(printerHost, printerPort, result, plate, vehicleClass);
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
            await ReportGateEventAsync(
                result.TicketId, gateCommandId, gateId, "OPEN", "FAILED",
                "Relay gate tidak dikonfigurasi.");
        }
        else
        {
            await ReportGateEventAsync(
                result.TicketId, gateCommandId, gateId, "OPEN", "SENT",
                "Perintah relay akan dikirim.");
            try
            {
                var baudRate = int.TryParse(Environment.GetEnvironmentVariable("PARKIR_RELAY_BAUD"), out var configuredBaudRate)
                    ? configuredBaudRate
                    : 9600;
                await Task.Run(() => SendRelayCommand(relayPortName, baudRate, relayCommand));
                statuses.Add("Perintah buka palang terkirim.");
                await ReportGateEventAsync(
                    result.TicketId, gateCommandId, gateId, "OPEN", "EXECUTED",
                    "Perintah relay serial terkirim.");
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or
                    InvalidOperationException or TimeoutException)
            {
                statuses.Add("Relay gagal; lakukan pembukaan manual.");
                await ReportGateEventAsync(
                    result.TicketId, gateCommandId, gateId, "OPEN", "FAILED",
                    "Relay serial gagal; lakukan pembukaan manual.");
            }
        }

        return string.Join(" ", statuses);
    }

    private static async Task ReportGateEventAsync(
        string ticketId,
        Guid commandId,
        string gateId,
        string action,
        string status,
        string? detail)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            using var response = await Client.PostAsJsonAsync(
                $"api/transactions/{Uri.EscapeDataString(ticketId)}/gate-event",
                new GateEventRequest(commandId, gateId, action, status, detail),
                JsonOptions,
                timeout.Token);
            _ = response.StatusCode;
        }
        catch (Exception)
        {
            // Event gate bersifat audit; kegagalan pelaporan tidak boleh menahan pembayaran.
        }
    }

    private static async Task PrintReceiptAsync(
        string host,
        int port,
        CheckoutResponse result,
        string? plate,
        string? vehicleClass)
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
        if (!string.IsNullOrWhiteSpace(plate))
        {
            WriteReceiptText(receipt, $"Plat    : {plate}\n");
        }
        if (!string.IsNullOrWhiteSpace(vehicleClass))
        {
            WriteReceiptText(receipt, $"Kelas   : {vehicleClass}\n");
        }
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
            CultureInfo.InvariantCulture)).ToArray();
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
        var content = new StringContent(pending.Payload, Encoding.UTF8, "application/json");
        return Client.PostAsync(
            $"api/transactions/{Uri.EscapeDataString(pending.TicketId)}/checkout", content);
    }

    private static HttpClient CreateClient()
    {
        var address = Environment.GetEnvironmentVariable("PARKIR_API_URL") ?? "http://127.0.0.1:5080/";
        var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
        var apiKey = PosSettings.DeviceKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        }
        client.DefaultRequestHeaders.Add("X-Device-Id", PosSettings.DeviceId);

        return client;
    }

    private static bool HasApiKey() =>
        !string.IsNullOrWhiteSpace(PosSettings.DeviceKey);

    private static string NormalizePlate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static decimal ParseAmount(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0m;
    }

    private void UpdateConnectionLabel()
    {
        if (!HasApiKey())
        {
            ConnectionLabel.Text = "PARKIR_DEVICE_KEY belum diatur";
            return;
        }

        var failedCount = _checkoutQueue.GetFailedCount();
        if (failedCount > 0)
        {
            ConnectionLabel.Text = $"{failedCount} checkout gagal - tinjau antrean";
            return;
        }
        if (IsSessionExpired)
        {
            ConnectionLabel.Text = "Sesi kasir berakhir - login ulang";
            return;
        }

        var outstanding = _checkoutQueue.GetOutstandingCount();
        ConnectionLabel.Text = outstanding == 0
            ? "API LAN siap"
            : $"{outstanding} checkout menunggu sinkronisasi";
    }

    private void UpdateQueueIssuesButton()
    {
        if (QueueIssuesButton is null)
        {
            return;
        }

        var failedCount = _checkoutQueue.GetFailedCount();
        QueueIssuesButton.Visibility = failedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueIssuesButton.Content = $"Antrean gagal ({failedCount})";
    }

    private async void QueueIssues_Click(object sender, RoutedEventArgs e)
    {
        var failed = _checkoutQueue.GetFailed();
        if (failed.Count == 0)
        {
            UpdateQueueIssuesButton();
            return;
        }

        var latest = failed[^1];
        var answer = MessageBox.Show(
            this,
            $"Ada {failed.Count} checkout yang ditolak server dan tidak akan dicoba otomatis.\n\n" +
            $"Tiket terakhir: {latest.TicketId}\nError: {latest.LastError}\n\n" +
            "Pastikan penyebabnya sudah diperbaiki, lalu coba ulang semua?",
            "Antrean checkout gagal",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _checkoutQueue.RetryFailed();
        UpdateConnectionLabel();
        UpdateQueueIssuesButton();
        await SyncPendingCheckoutsAsync();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        ClockLabel.Text = now;
        if (_currentQuote is not null)
        {
            DetailTimeOutText.Text = now;
        }
        UpdateSessionState();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            Checkout_Click(this, new RoutedEventArgs());
            return;
        }

        if (e.Key == Key.F4)
        {
            e.Handled = true;
            OpenPhotoViewer();
            return;
        }

        if (_isBusy)
        {
            return;
        }

        if (e.Key == Key.F1)
        {
            e.Handled = true;
            if (ClassMotorButton.IsEnabled) ClassMotorButton.IsChecked = true;
        }
        else if (e.Key == Key.F2)
        {
            e.Handled = true;
            if (ClassMobilButton.IsEnabled) ClassMobilButton.IsChecked = true;
        }
        else if (e.Key == Key.F3)
        {
            e.Handled = true;
            if (ClassTrukButton.IsEnabled) ClassTrukButton.IsChecked = true;
        }
        else if (e.Key == Key.F6)
        {
            e.Handled = true;
            if (LostTicketCheck.IsEnabled) LostTicketCheck.IsChecked = !(LostTicketCheck.IsChecked == true);
        }
        else if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }
        else if (e.Key == Key.T)
        {
            e.Handled = true;
            CashMethodButton.IsChecked = true;
        }
        else if (e.Key == Key.Q)
        {
            e.Handled = true;
            QrisMethodButton.IsChecked = true;
        }
        else if (e.Key == Key.D)
        {
            e.Handled = true;
            DebitMethodButton.IsChecked = true;
        }
        else if (e.Key == Key.OemQuestion)
        {
            e.Handled = true;
            ShowHelp();
        }
    }

    private void ZoomPhoto_Click(object sender, RoutedEventArgs e) => OpenPhotoViewer();

    private void OpenPhotoViewer()
    {
        if (EntryPhotoImage.Source is null && ExitPhotoImage.Source is null)
        {
            MessageLabel.Text = "Belum ada foto untuk diperbesar.";
            return;
        }

        var viewer = new Window
        {
            Title = "Perbandingan Foto Kendaraan",
            Owner = this,
            Width = 1000,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(243, 245, 241))
        };
        var grid = new Grid { Margin = new Thickness(18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var left = BuildPhotoPanel("Foto Kedatangan", EntryPhotoImage.Source);
        Grid.SetColumn(left, 0);
        var right = BuildPhotoPanel("Kamera Keluar · LIVE", ExitPhotoImage.Source);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        viewer.Content = grid;
        viewer.ShowDialog();
    }

    private static StackPanel BuildPhotoPanel(string title, ImageSource? source)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var border = new Border
        {
            Height = 420,
            Background = new SolidColorBrush(Color.FromRgb(247, 250, 247)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(221, 228, 222)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new Image { Source = source, Stretch = Stretch.Uniform }
        };
        panel.Children.Add(border);
        return panel;
    }

    private void Help_Click(object sender, RoutedEventArgs e) => ShowHelp();

    private void ShowHelp() =>
        MessageBox.Show(
            "F1 Motor\nF2 Mobil\nF3 Truk\nF4 Perbesar foto\nF6 Karcis hilang\nT / Q / D Metode bayar\nCtrl+Enter Cetak struk & buka gerbang\nEnter Lanjut ke input berikutnya",
            "Keyboard Shortcut",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private static Task<string> GetErrorMessageAsync(Exception exception) =>
        Task.FromResult(exception switch
        {
            HttpRequestException => "API tidak dapat dijangkau. Periksa server LAN dan alamat API.",
            TaskCanceledException => "Waktu koneksi ke API habis. Periksa jaringan LAN.",
            _ => exception.Message
        });

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
        string PaymentStatus,
        long VehicleTypeId,
        string VehicleTypeName);

    private sealed record CheckoutRequest(
        string GateId,
        string PaymentMethod,
        bool LostTicket,
        bool IdentityVerified,
        string? PhotoOutPath,
        Guid IdempotencyKey,
        long? VehicleTypeId,
        string? LicensePlate,
        string? OverrideReason,
        decimal? ExpectedTotalFee);

    private sealed record CheckoutResponse(
        string TicketId,
        int DurationMinutes,
        decimal TotalFee,
        decimal PenaltyFee,
        string PaymentStatus,
        DateTimeOffset TimeOut);

    private sealed record GateEventRequest(
        Guid CommandId,
        string GateId,
        string Action,
        string Status,
        string? Detail);

    private sealed record PhotoUploadResponse(string Path);

    private sealed record VehicleTypeDto(long Id, string TypeName);

    private sealed record DeviceHeartbeatRequest(
        string DeviceId,
        string DeviceType,
        string DisplayName,
        string? FirmwareVersion);

    private enum CheckoutOutcomeKind
    {
        Success,
        Retry,
        Failed
    }

    private sealed record CheckoutOutcome(
        CheckoutOutcomeKind Kind,
        CheckoutResponse? Result = null,
        string? Error = null);
}

public sealed record RecentTransaction(string Time, string Label, string Amount);
