using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Parkir.Api.Data;
using Parkir.Api.Models;

namespace Parkir.Api.Services;

public sealed class AuthService(
    AuthRepository repository,
    AuditRepository auditRepository,
    IPasswordHasher<UserAccount> passwordHasher,
    IConfiguration configuration)
{
    public async Task<LoginResponse> LoginAsync(
        LoginRequest request,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            throw new ArgumentException("Username dan password wajib diisi.");
        }

        var user = await repository.FindByUsernameAsync(request.Username.Trim(), cancellationToken);
        if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password)
            == PasswordVerificationResult.Failed)
        {
            await auditRepository.WriteAsync(
                null, "LOGIN_FAILED", "USER", request.Username.Trim(), "{}", sourceIp, cancellationToken);
            throw new UnauthorizedAccessException("Username atau password tidak valid.");
        }

        await auditRepository.WriteAsync(
            user.Id, "LOGIN_SUCCEEDED", "USER", user.Id.ToString(), "{}", sourceIp, cancellationToken);

        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);
        var signingKey = Encoding.UTF8.GetBytes(configuration["Security:JwtSigningKey"]!);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(signingKey), SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role)
        };
        var token = new JwtSecurityToken(claims: claims, expires: expiresAt.UtcDateTime, signingCredentials: credentials);
        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), user.Id, user.FullName, user.Role, expiresAt);
    }
}