using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Parkir.Api.Security;
using Parkir.Api.Services;

namespace Parkir.Api.Controllers;

[ApiController]
[Route("api/media")]
public sealed class MediaController(ImageStorageService imageStorage) : ControllerBase
{
    [HttpPost("entries")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
    [EnableRateLimiting("device")]
    [Consumes("image/jpeg", "image/png", "image/webp")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadEntry(CancellationToken cancellationToken)
    {
        try
        {
            var image = await imageStorage.StoreAsync(Request.Body, Request.ContentLength, Request.ContentType, cancellationToken);
            return Created($"/api/media/{image.Path}", new { path = image.Path });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("exits")]
    [Authorize(Roles = "CASHIER")]
    [Consumes("image/jpeg", "image/png", "image/webp")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadExit(CancellationToken cancellationToken)
    {
        try
        {
            var image = await imageStorage.StoreAsync(Request.Body, Request.ContentLength, Request.ContentType, cancellationToken);
            return Created($"/api/media/{image.Path}", new { path = image.Path });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("{date}/{fileName}")]
    [Authorize(Roles = "CASHIER,ADMIN")]
    public IActionResult GetImage(string date, string fileName)
    {
        var image = imageStorage.Find(date, fileName);
        return image is null ? NotFound() : PhysicalFile(image.FullPath, image.ContentType);
    }
}
