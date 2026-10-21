using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Parkir.Api.Models;
using Parkir.Api.Services;

namespace Parkir.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authService.LoginAsync(
                request,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return Unauthorized(new { error = exception.Message });
        }
    }
}