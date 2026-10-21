using Parkir.Api.Data;
using Parkir.Api.Models;

namespace Parkir.Api.Services;

public sealed class AdminService(AdminRepository repository, AuditRepository auditRepository)
{
    public async Task RecordDeviceHeartbeatAsync(
        DeviceHeartbeatRequest request,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 50 ||
            request.DeviceId.Any(character => !char.IsLetterOrDigit(character) && character is not ('-' or '_' or '.')))
        {
            throw new ArgumentException("DeviceId tidak valid.");
        }

        if (request.DeviceType is not ("GATE_IN" or "CASHIER_POS") ||
            string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 100 ||
            request.FirmwareVersion?.Length > 50)
        {
            throw new ArgumentException("Data heartbeat perangkat tidak valid.");
        }

        await repository.RecordDeviceHeartbeatAsync(request, sourceIp, cancellationToken);
    }

    public Task<IReadOnlyList<DeviceStatusDto>> GetDeviceStatusesAsync(CancellationToken cancellationToken) =>
        repository.GetDeviceStatusesAsync(cancellationToken);

    public Task<DashboardSummary> GetDashboardAsync(CancellationToken cancellationToken) =>
        repository.GetDashboardAsync(cancellationToken);

    public async Task<ParkingReport> GetReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (from == default || to == default || from > to || to.DayNumber - from.DayNumber > 365 ||
            to > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new ArgumentException("Rentang laporan tidak valid; pilih maksimal 366 hari hingga hari ini.");
        }

        var days = await repository.GetReportAsync(from, to, cancellationToken);
        return new ParkingReport(
            from,
            to,
            days,
            days.Sum(day => day.Entries),
            days.Sum(day => day.Exits),
            days.Sum(day => day.Revenue));
    }

    public Task<IReadOnlyList<VehicleTypeDto>> GetVehicleTypesAsync(CancellationToken cancellationToken) =>
        repository.GetVehicleTypesAsync(cancellationToken);

    public async Task<long> CreateVehicleTypeAsync(
        VehicleTypeInput input, long actorId, string? sourceIp, CancellationToken cancellationToken)
    {
        ValidateVehicleType(input);
        var id = await repository.CreateVehicleTypeAsync(input, cancellationToken);
        await auditRepository.WriteAsync(actorId, "VEHICLE_TYPE_CREATED", "VEHICLE_TYPE", id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(input), sourceIp, cancellationToken);
        return id;
    }

    public async Task<bool> UpdateVehicleTypeAsync(
        long id, VehicleTypeInput input, long actorId, string? sourceIp, CancellationToken cancellationToken)
    {
        ValidateVehicleType(input);
        var updated = await repository.UpdateVehicleTypeAsync(id, input, cancellationToken);
        if (updated)
        {
            await auditRepository.WriteAsync(actorId, "VEHICLE_TYPE_UPDATED", "VEHICLE_TYPE", id.ToString(),
                System.Text.Json.JsonSerializer.Serialize(input), sourceIp, cancellationToken);
        }

        return updated;
    }

    public Task<IReadOnlyList<MemberDto>> GetMembersAsync(CancellationToken cancellationToken) =>
        repository.GetMembersAsync(cancellationToken);

    public async Task<long> CreateMemberAsync(
        MemberInput input, long actorId, string? sourceIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.RfidUid) || string.IsNullOrWhiteSpace(input.FullName) ||
            string.IsNullOrWhiteSpace(input.LicensePlate) || input.ExpiredDate < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new ArgumentException("UID RFID, nama, plat nomor, dan tanggal aktif yang valid wajib diisi.");
        }

        var id = await repository.CreateMemberAsync(input, cancellationToken);
        await auditRepository.WriteAsync(actorId, "MEMBER_CREATED", "MEMBER", id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(input), sourceIp, cancellationToken);
        return id;
    }

    public async Task<bool> UpdateMemberStatusAsync(
        long id, string status, long actorId, string? sourceIp, CancellationToken cancellationToken)
    {
        status = status.Trim().ToUpperInvariant();
        if (status is not ("ACTIVE" or "EXPIRED" or "SUSPENDED"))
        {
            throw new ArgumentException("Status member harus ACTIVE, EXPIRED, atau SUSPENDED.");
        }

        var updated = await repository.UpdateMemberStatusAsync(id, status, cancellationToken);
        if (updated)
        {
            await auditRepository.WriteAsync(actorId, "MEMBER_STATUS_UPDATED", "MEMBER", id.ToString(),
                System.Text.Json.JsonSerializer.Serialize(new { status }), sourceIp, cancellationToken);
        }

        return updated;
    }

    private static void ValidateVehicleType(VehicleTypeInput input)
    {
        if (string.IsNullOrWhiteSpace(input.TypeName) || input.FirstHourRate < 0 || input.HourlyRate < 0 ||
            input.GracePeriodMinutes < 0 || input.LostTicketFee < 0 || input.MaxDailyRate < 0)
        {
            throw new ArgumentException("Nama kendaraan dan tarif tidak boleh kosong atau negatif.");
        }
    }
}
