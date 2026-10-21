using Npgsql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Parkir.Api.Data;
using Parkir.Api.Models;
using Parkir.Api.Services;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Parking")
    ?? "Host=localhost;Port=5432;Database=parkir;Username=postgres;Password=postgres";
var apiKey = builder.Configuration["Security:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("Security:ApiKey wajib dikonfigurasi sebelum API dijalankan.");
}
var jwtSigningKey = builder.Configuration["Security:JwtSigningKey"];
if (string.IsNullOrWhiteSpace(jwtSigningKey) || Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
{
    throw new InvalidOperationException("Security:JwtSigningKey wajib diisi minimal 32 byte.");
}

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<ParkingRepository>();
builder.Services.AddScoped<ParkingService>();
builder.Services.AddScoped<AuthRepository>();
builder.Services.AddScoped<AuditRepository>();
builder.Services.AddScoped<AdminRepository>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddSingleton(new ImageStorageService(
    builder.Environment,
    builder.Configuration["Storage:ImageRoot"]));
builder.Services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Terlalu banyak percobaan login. Coba lagi beberapa menit." }, cancellationToken);
    };
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var deviceEndpoint = path.Equals("/api/entries", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/members", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/media/entries", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/devices/heartbeat", StringComparison.OrdinalIgnoreCase);
    if (deviceEndpoint)
    {
        var suppliedKey = context.Request.Headers["X-API-Key"].ToString();
        var expectedBytes = Encoding.UTF8.GetBytes(apiKey);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
        if (expectedBytes.Length != suppliedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "API key perangkat tidak valid." });
            return;
        }
    }

    await next();
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();