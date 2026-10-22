using System.Security.Claims;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parkir.Api.Models;
using Parkir.Api.Services;

namespace Parkir.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "ADMIN")]
public sealed class AdminController(AdminService adminService) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardSummary>> GetDashboard(CancellationToken cancellationToken) =>
        Ok(await adminService.GetDashboardAsync(cancellationToken));

    [HttpGet("devices")]
    public async Task<ActionResult<IReadOnlyList<DeviceStatusDto>>> GetDevices(CancellationToken cancellationToken) =>
        Ok(await adminService.GetDeviceStatusesAsync(cancellationToken));

    [HttpGet("gates")]
    public async Task<ActionResult<IReadOnlyList<GateDto>>> GetGates(CancellationToken cancellationToken) =>
        Ok(await adminService.GetGatesAsync(cancellationToken));

    [HttpPost("gates")]
    public async Task<IActionResult> CreateGate(GateInput input, CancellationToken cancellationToken)
    {
        try
        {
            var actorId = GetActorId();
            var created = await adminService.CreateGateAsync(
                input, actorId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
            return created ? Created($"/api/admin/gates/{input.GateId}", new { gateId = input.GateId }) : Conflict();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPut("gates/{gateId}")]
    public async Task<IActionResult> UpdateGate(string gateId, GateInput input, CancellationToken cancellationToken)
    {
        try
        {
            var actorId = GetActorId();
            var updated = await adminService.UpdateGateAsync(
                gateId, input, actorId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
            return updated ? NoContent() : NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("reports")]
    public async Task<ActionResult<ParkingReport>> GetReport(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await adminService.GetReportAsync(from, to, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("reports/export.csv")]
    public async Task<IActionResult> ExportReport(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await adminService.GetReportAsync(from, to, cancellationToken);
            var csv = new StringBuilder("Tanggal,Jumlah Masuk,Jumlah Keluar,Pendapatan\r\n");
            foreach (var day in report.Days)
            {
                csv.Append(day.ReportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                    .Append(day.Entries.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(day.Exits.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(day.Revenue.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            }

            csv.Append("TOTAL,")
                .Append(report.TotalEntries.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(report.TotalExits.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(report.TotalRevenue.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            var content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            return File(content, "text/csv; charset=utf-8", $"laporan-parkir-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("vehicle-types")]
    public async Task<ActionResult<IReadOnlyList<VehicleTypeDto>>> GetVehicleTypes(CancellationToken cancellationToken) =>
        Ok(await adminService.GetVehicleTypesAsync(cancellationToken));

    [HttpPost("vehicle-types")]
    public async Task<IActionResult> CreateVehicleType(VehicleTypeInput input, CancellationToken cancellationToken)
    {
        try
        {
            var actorId = GetActorId();
            var id = await adminService.CreateVehicleTypeAsync(
                input, actorId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
            return Created($"/api/admin/vehicle-types/{id}", new { id });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPut("vehicle-types/{id:long}")]
    public async Task<IActionResult> UpdateVehicleType(long id, VehicleTypeInput input, CancellationToken cancellationToken)
    {
        try
        {
            var actorId = GetActorId();
            var updated = await adminService.UpdateVehicleTypeAsync(
                id, input, actorId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
            return updated ? NoContent() : NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("members")]
    public async Task<ActionResult<IReadOnlyList<MemberDto>>> GetMembers(CancellationToken cancellationToken) =>
        Ok(await adminService.GetMembersAsync(cancellationToken));

    [HttpPost("members")]
    public async Task<IActionResult> CreateMember(MemberInput input, CancellationToken cancellationToken)
    {
        try
        {
            var actorId = GetActorId();
            var id = await adminService.CreateMemberAsync(
                input, actorId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
            return Created($"/api/admin/members/{id}", new { id });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPut("members/{id:long}/status")]
    public async Task<IActionResult> UpdateMemberStatus(
        long id, MemberStatusInput input, CancellationToken cancellationToken)
    {
        try
        {
            var actorId = GetActorId();
            var updated = await adminService.UpdateMemberStatusAsync(
                id, input.Status, actorId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
            return updated ? NoContent() : NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    private long GetActorId() =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new UnauthorizedAccessException("ID admin tidak valid.");
}