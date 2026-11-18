using Npgsql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Parkir.Api.Data;
using Parkir.Api.Models;
using Parkir.Api.Security;
using Parkir.Api.Services;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Parking");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Parking wajib dikonfigurasi. Fallback kredensial database tidak diizinkan.");
}
var jwtSigningKey = builder.Configuration["Security:JwtSigningKey"];
if (string.IsNullOrWhiteSpace(jwtSigningKey) || Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
{
    throw new InvalidOperationException("Security:JwtSigningKey wajib diisi minimal 32 byte.");
}

builder.Services.AddSingleton<BusinessClock>();
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<ParkingRepository>();
builder.Services.AddScoped<GateRepository>();
builder.Services.AddScoped<ParkingService>();
builder.Services.AddScoped<AuthRepository>();
builder.Services.AddScoped<AuditRepository>();
builder.Services.AddScoped<AdminRepository>();
builder.Services.AddScoped<DeviceCredentialRepository>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddSingleton(sp => new ImageStorageService(
    builder.Environment,
    builder.Configuration["Storage:ImageRoot"],
    sp.GetRequiredService<BusinessClock>()));
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
    options.AddPolicy("device", context =>
    {
        var partition = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-device";
        return RateLimitPartition.GetFixedWindowLimiter(
            partition,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
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
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 100)
    {
        correlationId = context.TraceIdentifier;
    }

    context.TraceIdentifier = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    using var scope = app.Logger.BeginScope(new Dictionary<string, object>
    {
        ["CorrelationId"] = correlationId
    });
    await next();
});
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var exception = feature?.Error;
    app.Logger.LogError(exception, "Permintaan gagal tanpa penanganan. TraceId={TraceId}", context.TraceIdentifier);

    var (statusCode, message) = exception switch
    {
        PostgresException { SqlState: "23505" } => (StatusCodes.Status409Conflict, "Data sudah ada."),
        PostgresException { SqlState: "23503" } => (StatusCodes.Status400BadRequest, "Data terkait tidak ditemukan."),
        PostgresException { SqlState: "23514" } => (StatusCodes.Status400BadRequest, "Data melanggar aturan validasi."),
        PostgresException { SqlState: "22001" } => (StatusCodes.Status400BadRequest, "Data melebihi panjang yang diizinkan."),
        _ => (StatusCodes.Status500InternalServerError, "Terjadi kesalahan internal.")
    };
    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "application/json; charset=utf-8";
    await context.Response.WriteAsJsonAsync(new { error = message, traceId = context.TraceIdentifier });
}));
if (app.Configuration.GetValue("Security:RequireHttps", false))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", async (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
{
    try
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
        return Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow });
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Health check database gagal.");
        return Results.Json(
            new { status = "unhealthy" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});
app.Run();

public partial class Program;