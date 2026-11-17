using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Npgsql;
using Parkir.Api.Data;

namespace Parkir.Api.Security;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    DeviceCredentialRepository credentialRepository)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DeviceApiKey";
    public const string DeviceIdClaim = "device_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var deviceId = Request.Headers["X-Device-Id"].ToString().Trim();
        var suppliedKey = Request.Headers["X-API-Key"].ToString();
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 50 ||
            deviceId.Any(character => !char.IsLetterOrDigit(character) && character is not ('-' or '_' or '.')) ||
            string.IsNullOrWhiteSpace(suppliedKey))
        {
            return AuthenticateResult.Fail("Identitas atau API key perangkat tidak valid.");
        }

        string? storedHash;
        try
        {
            storedHash = await credentialRepository.GetActiveHashAsync(deviceId, Context.RequestAborted);
        }
        catch (NpgsqlException exception)
        {
            Logger.LogError(exception, "Gagal memeriksa kredensial perangkat {DeviceId}.", deviceId);
            return AuthenticateResult.Fail("Kredensial perangkat tidak dapat diverifikasi.");
        }

        if (storedHash is null || !DeviceKeyHasher.Verify(storedHash, suppliedKey))
        {
            return AuthenticateResult.Fail("API key perangkat tidak valid.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, deviceId),
            new Claim(DeviceIdClaim, deviceId),
            new Claim(ClaimTypes.Role, "DEVICE")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json; charset=utf-8";
        await Response.WriteAsync("""{"error":"API key perangkat tidak valid."}""");
    }
}
