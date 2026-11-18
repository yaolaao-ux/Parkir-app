using Parkir.Api.Data;
using Parkir.Api.Models;
using Parkir.Api.Security;

namespace Parkir.Api.Services;

public sealed class AdminService(
    AdminRepository repository,
    DeviceCredentialRepository credentialRepository,
    BusinessClock clock)
{
    public Task<IReadOnlyList<GateDto>> GetGatesAsync(CancellationToken cancellationToken) =>
        repository.GetGatesAsync(cancellationToken);

    public async Task<bool> CreateGateAsync(
        GateInput input, long actorId, string? sourceIp, CancellationToken cancellationToken)
    {
        ValidateGate(input);
        return await repository.CreateGateAsync(input, actorId, sourceIp, cancellationToken);
    }

    public async Task<bool> UpdateGateAsync(
        string gateId, GateInput input, long actorId, string? sourceIp, CancellationToken cancellationToken)
    {
        ValidateGate(input);
        if (!string.Equals(gateId, input.GateId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("GateId tidak dapat diubah; daftarkan gate baru untuk mengganti ID.");
        }

        return await repository.UpdateGateAsync(gateId, input, actorId, sourceIp, cancellationToken);
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

    public async Task<DeviceCredentialResponse> IssueDeviceCredentialAsync(
        string deviceId,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        var normalized = ValidateDeviceId(deviceId);
        var apiKey = DeviceKeyHasher.GenerateKey();
        await credentialRepository.IssueAsync(
            normalized, DeviceKeyHasher.Hash(apiKey), actorId, sourceIp, cancellationToken);
        return new DeviceCredentialResponse(normalized, apiKey);
    }

    public Task<bool> RevokeDeviceCredentialAsync(
        string deviceId,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        var normalized = ValidateDeviceId(deviceId);
        return credentialRepository.RevokeAsync(normalized, actorId, sourceIp, cancellationToken);
    }

    public async Task<bool> SetUserStatusAsync(
        string username,
        bool isActive,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        var normalized = username?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 50)
        {
            throw new ArgumentException("Username tidak valid.");
        }

        return await repository.SetUserStatusAsync(normalized, isActive, actorId, sourceIp, cancellationToken);
    }

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
            days.Sum(day => day.Revenue),
            days.Sum(day => day.PaidTransactions),
            days.Sum(day => day.VoidTransactions),
            days.Sum(day => day.UnpaidEntries));
    }

    public Task<IReadOnlyList<VehicleTypeDto>> GetVehicleTypesAsync(CancellationToken cancellationToken) =>
        repository.GetVehicleTypesAsync(cancellationToken);

    public async Task<long> CreateVehicleTypeAsync(
        VehicleTypeInput input, long actorId, string? sourceIp, CancellationToken cancellationToken)
    {
        ValidateVehicleType(input);
        return await repository.CreateVehicleTypeAsync(input, actorId, sourceIp, cancellationToken);
    }

    public async Task<bool> UpdateVehicleTypeAsync(
        long id, VehicleTypeInput input, long actorId, string? sourceIp, CancellationToken cancellationToken)
    {
        ValidateVehicleType(input);
        return await repository.UpdateVehicleTypeAsync(id, input, actorId, sourceIp, cancellationToken);
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

        return await repository.CreateMemberAsync(input, actorId, sourceIp, cancellationToken);
    }

    public async Task<bool> UpdateMemberAsync(
        long id,
        MemberUpdateInput input,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.FullName) || string.IsNullOrWhiteSpace(input.LicensePlate) ||
            input.ExpiredDate < clock.Today)
        {
            throw new ArgumentException("Nama, plat nomor, dan tanggal aktif yang valid wajib diisi.");
        }

        return await repository.UpdateMemberAsync(id, input, actorId, sourceIp, cancellationToken);
    }

    public async Task<bool> UpdateMemberStatusAsync(
        long id, string status, long actorId, string? sourceIp, CancellationToken cancellationToken)
    {
        status = status.Trim().ToUpperInvariant();
        if (status is not ("ACTIVE" or "EXPIRED" or "SUSPENDED"))
        {
            throw new ArgumentException("Status member harus ACTIVE, EXPIRED, atau SUSPENDED.");
        }

        return await repository.UpdateMemberStatusAsync(id, status, actorId, sourceIp, cancellationToken);
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

    private static string ValidateDeviceId(string deviceId)
    {
        var normalized = deviceId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 50 ||
            normalized.Any(character => !char.IsLetterOrDigit(character) && character is not ('-' or '_' or '.')))
        {
            throw new ArgumentException("DeviceId tidak valid.");
        }

        return normalized;
    }
}
