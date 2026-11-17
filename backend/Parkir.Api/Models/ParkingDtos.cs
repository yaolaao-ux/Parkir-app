namespace Parkir.Api.Models;

public sealed record EntryRequest(
    string GateId,
    long VehicleTypeId,
    string? PhotoInPath,
    string? RfidUid,
    string? IdempotencyKey = null);

public sealed record EntryResponse(
    string TicketId,
    DateTimeOffset TimeIn,
    string? MemberName,
    bool IsNew = true);

public sealed record QuoteResponse(
    string TicketId,
    DateTimeOffset TimeIn,
    int DurationMinutes,
    decimal TotalFee,
    decimal LostTicketTotal,
    string? PhotoInPath,
    bool IsMember,
    string PaymentStatus,
    long VehicleTypeId,
    string VehicleTypeName);

public sealed record CheckoutRequest(
    string GateId,
    string PaymentMethod,
    bool LostTicket,
    bool IdentityVerified,
    string? PhotoOutPath,
    Guid IdempotencyKey,
    long? VehicleTypeId = null,
    string? LicensePlate = null,
    string? OverrideReason = null,
    decimal? ExpectedTotalFee = null);

public sealed record VehicleClassOverride(
    long OriginalVehicleTypeId,
    string OriginalVehicleTypeName,
    string Reason,
    decimal OriginalTotalFee);

public sealed record VoidTicketRequest(string Reason);

public sealed record CheckoutResponse(
    string TicketId,
    int DurationMinutes,
    decimal TotalFee,
    decimal PenaltyFee,
    string PaymentStatus,
    DateTimeOffset TimeOut);

public sealed record MemberVerificationResponse(
    bool Valid,
    string? FullName,
    string? LicensePlate,
    DateOnly? ExpiredDate);

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(
    string AccessToken,
    long UserId,
    string FullName,
    string Role,
    DateTimeOffset ExpiresAt);

public sealed record UserAccount(
    long Id,
    string Username,
    string FullName,
    string Role,
    string PasswordHash);

public sealed record DashboardSummary(
    long VehiclesInside,
    long EntriesToday,
    long ExitsToday,
    decimal RevenueToday);

public sealed record VehicleTypeDto(
    long Id,
    string TypeName,
    decimal FirstHourRate,
    decimal HourlyRate,
    int GracePeriodMinutes,
    decimal LostTicketFee,
    decimal MaxDailyRate);

public sealed record VehicleTypeInput(
    string TypeName,
    decimal FirstHourRate,
    decimal HourlyRate,
    int GracePeriodMinutes,
    decimal LostTicketFee,
    decimal MaxDailyRate);

public sealed record MemberDto(
    long Id,
    string RfidUid,
    string FullName,
    string LicensePlate,
    long? VehicleTypeId,
    string? VehicleTypeName,
    DateOnly ExpiredDate,
    string Status);

public sealed record MemberInput(
    string RfidUid,
    string FullName,
    string LicensePlate,
    long? VehicleTypeId,
    DateOnly ExpiredDate);

public sealed record MemberStatusInput(string Status);

public sealed record DailyReportRow(
    DateOnly ReportDate,
    long Entries,
    long Exits,
    decimal Revenue);

public sealed record ParkingReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DailyReportRow> Days,
    long TotalEntries,
    long TotalExits,
    decimal TotalRevenue);

public sealed record DeviceHeartbeatRequest(
    string DeviceId,
    string DeviceType,
    string DisplayName,
    string? FirmwareVersion);

public sealed record DeviceStatusDto(
    string DeviceId,
    string DeviceType,
    string DisplayName,
    string? FirmwareVersion,
    string? IpAddress,
    DateTimeOffset LastSeen,
    bool IsOnline);

public sealed record DeviceCredentialResponse(string DeviceId, string ApiKey);

public sealed record UserStatusInput(bool IsActive);

public sealed record GateDto(
    string GateId,
    string GateType,
    string DisplayName,
    bool Enabled,
    bool IsOnline,
    DateTimeOffset? LastSeen);

public sealed record GateInput(
    string GateId,
    string GateType,
    string DisplayName,
    bool Enabled);

public sealed record TicketData(
    string TicketId,
    DateTimeOffset TimeIn,
    string? PhotoInPath,
    string PaymentStatus,
    bool IsMember,
    string? MemberName,
    int GracePeriodMinutes,
    decimal FirstHourRate,
    decimal HourlyRate,
    decimal LostTicketFee,
    decimal MaxDailyRate,
    long VehicleTypeId,
    string VehicleTypeName);