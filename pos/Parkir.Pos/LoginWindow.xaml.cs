using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace Parkir.Pos;

public partial class LoginWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public PosSession? Session { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        var address = Environment.GetEnvironmentVariable("PARKIR_API_URL") ?? "http://127.0.0.1:5080/";
        _client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
        var apiKey = Environment.GetEnvironmentVariable("PARKIR_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        }

        UsernameInput.Focus();
    }

    private async void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await LoginAsync();
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e) => await LoginAsync();

    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PARKIR_API_KEY")))
        {
            MessageLabel.Text = "PARKIR_API_KEY belum dikonfigurasi.";
            return;
        }

        if (string.IsNullOrWhiteSpace(UsernameInput.Text) || string.IsNullOrEmpty(PasswordInput.Password))
        {
            MessageLabel.Text = "Username dan password wajib diisi.";
            return;
        }

        LoginButton.IsEnabled = false;
        try
        {
            using var response = await _client.PostAsJsonAsync(
                "api/auth/login",
                new LoginRequest(UsernameInput.Text.Trim(), PasswordInput.Password),
                JsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                MessageLabel.Text = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Username atau password tidak valid."
                    : $"Login gagal ({(int)response.StatusCode}). Periksa API dan konfigurasi akun.";
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
            if (result is null || result.Role != "CASHIER")
            {
                MessageLabel.Text = "Akun ini tidak memiliki akses kasir.";
                return;
            }

            Session = new PosSession(result.AccessToken, result.UserId, result.FullName, result.Role, result.ExpiresAt);
            DialogResult = true;
        }
        catch (HttpRequestException)
        {
            MessageLabel.Text = "API tidak dapat dijangkau. Periksa jaringan LAN.";
        }
        catch (JsonException)
        {
            MessageLabel.Text = "Response login dari server tidak valid.";
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private sealed record LoginRequest(string Username, string Password);

    private sealed record LoginResponse(
        string AccessToken,
        long UserId,
        string FullName,
        string Role,
        DateTimeOffset ExpiresAt);
}