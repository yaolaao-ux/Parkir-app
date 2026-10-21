using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parkir.Api.Models;
using Parkir.Api.Services;

namespace Parkir.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class ParkingController(ParkingService parkingService) : ControllerBase
{
    [HttpPost("entries")]
    [ProducesResponseType<EntryResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateEntry(EntryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await parkingService.CreateEntryAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetQuote), new { ticketId = result.TicketId }, result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("transactions/{ticketId}/quote")]
    [Authorize(Roles = "CASHIER")]
    public async Task<IActionResult> GetQuote(string ticketId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await parkingService.GetQuoteAsync(ticketId, cancellationToken));
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
    public async Task<ActionResult<MemberVerificationResponse>> VerifyMember(
        string rfidUid,
        CancellationToken cancellationToken) =>
        Ok(await parkingService.VerifyMemberAsync(rfidUid, cancellationToken));
}