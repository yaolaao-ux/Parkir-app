using Parkir.Api.Data;
using Parkir.Api.Models;

namespace Parkir.Api.Services;

public sealed class AdminService(AdminRepository repository, AuditRepository auditRepository, BusinessClock clock)
{
    public Task<IReadOnlyList<GateDto>> GetGatesAsync(CancellationToken cancellationToken) =>
        repository.GetGatesAsync(cancellationToken);

    public async Task<bool> CreateGateAsync(
        GateInput input, long actorId, string? sourceIp, CancellationToken cancellationToken)
    {
        ValidateGate(input);
        var created = await repository.CreateGateAsync(input, cancellationToken);
        if (created)
        {
            await auditRepository.WriteAsync(actorId, "GATE_CREATED", "GATE", input.GateId.Trim().ToUpperInvariant(),
                System.Text.Json.JsonSerializer.Serialize(input), sourceIp, cancellationToken);
        }

        return created;
    }

    public async Task<bool> UpdateGateAsync(
        string gateId, GateInput input, long actorId, string? sourceIp, CancellationToken cancellationToken)
    {
        ValidateGate(input);
        if (!string.Equals(gateId, input.GateId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("GateId tidak dapat diubah; daftarkan gate baru untuk mengganti ID.");
        }

        var updated = await repository.UpdateGateAsync(gateId, input, cancellationToken);
        if (updated)
        {
            await auditRepository.WriteAsync(actorId, "GATE_UPDATED", "GATE", gateId,
                System.Text.Json.JsonSerializer.Serialize(input), sourceIp, cancellationToken);
        }

        return updated;
    }

    public Task<bool> IsGateEnabledAsync(string gateId, string gateType, CancellationToken cancellationToken) =>
        repository.IsGateEnabledAsync(gateId, gateType, cancellationToken);

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
        repository.GetDashboardAsync(clock.DayStartUtc(clock.Today), clock.DayEndUtc(clock.Today), cancellationToken);

    public async Task<ParkingReport> GetReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (from == default || to == default || from > to || to.DayNumber - from.DayNumber > 365 ||
            to > clock.Today)
        {
            throw new ArgumentException("Rentang laporan tidak valid; pilih maksimal 366 hari hingga hari ini.");
        }

        var days = await repository.GetReportAsync(from, to, clock.TimeZoneId, cancellationToken);
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
            string.IsNullOrWhiteSpace(input.LicensePlate) || input.ExpiredDate < clock.Today)
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

    private static void ValidateGate(GateInput input)
    {
        if (string.IsNullOrWhiteSpace(input.GateId) || input.GateId.Length > 20 ||
            input.GateId.Any(character => !char.IsLetterOrDigit(character) && character is not ('-' or '_' or '.')) ||
            input.GateType is not ("GATE_IN" or "GATE_OUT") || string.IsNullOrWhiteSpace(input.DisplayName) ||
            input.DisplayName.Length > 100)
        {
            throw new ArgumentException("GateId, arah, atau nama gate tidak valid.");
        }
    }
}
