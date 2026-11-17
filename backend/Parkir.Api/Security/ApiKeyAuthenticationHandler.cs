using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Parkir.Api.Security;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DeviceApiKey";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expectedKey = configuration["Security:ApiKey"];
        var suppliedKey = Request.Headers["X-API-Key"].ToString();
        if (string.IsNullOrEmpty(expectedKey) || string.IsNullOrEmpty(suppliedKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("API key perangkat tidak valid."));
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
        if (expectedBytes.Length != suppliedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
        {
            return Task.FromResult(AuthenticateResult.Fail("API key perangkat tidak valid."));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "parkir-device") };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json; charset=utf-8";
        await Response.WriteAsync("""{"error":"API key perangkat tidak valid."}""");
    }
}
