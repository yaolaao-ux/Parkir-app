namespace Parkir.Api.Models;

public sealed record EntryRequest(
    string GateId,
    long VehicleTypeId,
    string? PhotoInPath,
    string? RfidUid);

public sealed record EntryResponse(
    string TicketId,
    DateTimeOffset TimeIn,
    string? MemberName);

public sealed record QuoteResponse(
    string TicketId,
    DateTimeOffset TimeIn,
    int DurationMinutes,
    decimal TotalFee,
    decimal LostTicketTotal,
    string? PhotoInPath,
    bool IsMember,
    string PaymentStatus);

public sealed record CheckoutRequest(
    string GateId,
    string PaymentMethod,
    bool LostTicket,
    bool IdentityVerified,
    string? PhotoOutPath,
    Guid IdempotencyKey);

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
    decimal MaxDailyRate);