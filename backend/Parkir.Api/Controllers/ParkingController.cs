using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parkir.Api.Models;
using Parkir.Api.Security;
using Parkir.Api.Services;

namespace Parkir.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class ParkingController(ParkingService parkingService, AdminService adminService) : ControllerBase
{
    [HttpGet("vehicle-types")]
    [Authorize(Roles = "CASHIER")]
    public async Task<ActionResult<IReadOnlyList<VehicleTypeDto>>> GetVehicleTypes(CancellationToken cancellationToken) =>
        Ok(await adminService.GetVehicleTypesAsync(cancellationToken));

    [HttpPost("entries")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
    [ProducesResponseType<EntryResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateEntry(EntryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await parkingService.CreateEntryAsync(request, cancellationToken);
            return result.IsNew
                ? CreatedAtAction(nameof(GetQuote), new { ticketId = result.TicketId }, result)
                : Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (IdempotencyConflictException exception)
        {
            return Conflict(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("transactions/{ticketId}/void")]
    [Authorize(Roles = "ADMIN,CASHIER")]
    public async Task<IActionResult> VoidTicket(
        string ticketId,
        VoidTicketRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!long.TryParse(userId, out var actorUserId))
            {
                return Unauthorized();
            }

            await parkingService.VoidTicketAsync(
                ticketId,
                request,
                actorUserId,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);
            return NoContent();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpGet("transactions/{ticketId}/quote")]
    [Authorize(Roles = "CASHIER")]
    public async Task<IActionResult> GetQuote(
        string ticketId,
        [FromQuery] long? vehicleTypeId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await parkingService.GetQuoteAsync(ticketId, vehicleTypeId, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpPost("transactions/{ticketId}/checkout")]
    [Authorize(Roles = "CASHIER")]
    public async Task<IActionResult> Checkout(
        string ticketId,
        CheckoutRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!long.TryParse(userId, out var cashierUserId))
            {
                return Unauthorized();
            }

            return Ok(await parkingService.CheckoutAsync(
                ticketId,
                request,
                cashierUserId,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpGet("members/{rfidUid}/verify")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
    public async Task<ActionResult<MemberVerificationResponse>> VerifyMember(
        string rfidUid,
        CancellationToken cancellationToken) =>
        Ok(await parkingService.VerifyMemberAsync(rfidUid, cancellationToken));
}

[ApiController]
[Route("api/devices")]
public sealed class DeviceController(AdminService adminService) : ControllerBase
{
    [HttpPost("heartbeat")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
    public async Task<IActionResult> Heartbeat(DeviceHeartbeatRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await adminService.RecordDeviceHeartbeatAsync(
                request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
            return NoContent();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}