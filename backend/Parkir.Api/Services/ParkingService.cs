using Parkir.Api.Data;
using Parkir.Api.Models;

namespace Parkir.Api.Services;

public sealed class ParkingService(
    ParkingRepository repository,
    AdminService adminService,
    BusinessClock clock,
    IConfiguration configuration,
    ILogger<ParkingService> logger)
{
    public async Task<EntryResponse> CreateEntryAsync(EntryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.GateId) || request.VehicleTypeId <= 0)
        {
            throw new ArgumentException("GateId dan VehicleTypeId wajib diisi.");
        }
        var idempotencyKey = NormalizeEntryKey(request.IdempotencyKey);
        if (!await adminService.IsGateEnabledAsync(request.GateId, "GATE_IN", cancellationToken))
        {
            throw new InvalidOperationException("Gate masuk belum terdaftar atau sedang dinonaktifkan.");
        }

        var timeIn = clock.UtcNow;
        var ticketId = TicketIdGenerator.Create(clock.Now);
        try
        {
            var result = await repository.CreateEntryAsync(
                ticketId, request, timeIn, idempotencyKey, EntryRequestFingerprint.Create(request), cancellationToken);
            return new EntryResponse(result.TicketId, result.TimeIn, result.MemberName, result.IsNew);
        }
        catch (IdempotencyConflictException exception)
        {
            logger.LogWarning(
                "Entry idempotency conflict untuk gate {GateId} dan key {IdempotencyKey}: {Message}",
                request.GateId, request.IdempotencyKey, exception.Message);
            throw;
        }
    }

    public async Task<QuoteResponse> GetQuoteAsync(
        string ticketId,
        long? vehicleTypeId,
        CancellationToken cancellationToken)
    {
        var ticket = await repository.GetTicketAsync(ticketId, cancellationToken);
        if (ticket.PaymentStatus != "UNPAID")
        {
            throw new InvalidOperationException("Tiket sudah diproses dan tidak dapat digunakan kembali.");
        }

        var effectiveTicket = ticket;
        if (vehicleTypeId is { } selectedTypeId)
        {
            effectiveTicket = ApplyVehicleType(ticket, await GetVehicleTypeAsync(selectedTypeId, cancellationToken));
        }

        var duration = ParkingCalculator.GetDurationMinutes(effectiveTicket.TimeIn, clock.UtcNow);
        var fee = ParkingCalculator.CalculateFee(effectiveTicket, duration);
        return new QuoteResponse(
            effectiveTicket.TicketId,
            effectiveTicket.TimeIn,
            duration,
            fee,
            ParkingCalculator.CalculateLostTicketTotal(effectiveTicket),
            effectiveTicket.PhotoInPath,
            effectiveTicket.IsMember,
            effectiveTicket.PaymentStatus,
            effectiveTicket.VehicleTypeId,
            effectiveTicket.VehicleTypeName);
    }

    public async Task<CheckoutResponse> CheckoutAsync(
        string ticketId,
        CheckoutRequest request,
        long cashierUserId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        if (request.IdempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("IdempotencyKey wajib berupa UUID yang valid.");
        }

        var previousCheckout = await repository.GetCompletedCheckoutAsync(request.IdempotencyKey, cancellationToken);
        if (previousCheckout is not null)
        {
            if (previousCheckout.TicketId != ticketId)
            {
                throw new InvalidOperationException("IdempotencyKey sudah digunakan untuk tiket lain.");
            }

            return previousCheckout;
        }

        if (string.IsNullOrWhiteSpace(request.GateId))
        {
            throw new ArgumentException("GateId wajib diisi.");
        }
        if (!await adminService.IsGateEnabledAsync(request.GateId, "GATE_OUT", cancellationToken))
        {
            throw new InvalidOperationException("Gate keluar belum terdaftar atau sedang dinonaktifkan.");
        }

        if (!request.LostTicket && string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            throw new ArgumentException("PaymentMethod wajib diisi.");
        }

        if (request.LostTicket && !request.IdentityVerified)
        {
            throw new ArgumentException("Verifikasi identitas wajib untuk karcis hilang.");
        }

        var licensePlate = NormalizeLicensePlate(request.LicensePlate);
        if (licensePlate is null && configuration.GetValue("Checkout:RequireLicensePlate", true))
        {
            throw new ArgumentException("Plat nomor wajib diisi saat checkout.");
        }

        var timeOut = clock.UtcNow;
        var ticket = await repository.GetTicketAsync(ticketId, cancellationToken);
        if (ticket.PaymentStatus != "UNPAID")
        {
            throw new InvalidOperationException("Tiket sudah diproses dan tidak dapat digunakan kembali.");
        }

        var duration = ParkingCalculator.GetDurationMinutes(ticket.TimeIn, timeOut);
        var originalTotal = request.LostTicket
            ? ParkingCalculator.CalculateLostTicketTotal(ticket)
            : ParkingCalculator.CalculateFee(ticket, duration);

        var effectiveTicket = ticket;
        VehicleClassOverride? classOverride = null;
        if (request.VehicleTypeId is { } selectedTypeId && selectedTypeId != ticket.VehicleTypeId)
        {
            classOverride = new VehicleClassOverride(
                ticket.VehicleTypeId,
                ticket.VehicleTypeName,
                NormalizeOverrideReason(request.OverrideReason),
                originalTotal);
            effectiveTicket = ApplyVehicleType(ticket, await GetVehicleTypeAsync(selectedTypeId, cancellationToken));
        }

        var penalty = request.LostTicket ? effectiveTicket.LostTicketFee : 0m;
        var totalFee = request.LostTicket
            ? ParkingCalculator.CalculateLostTicketTotal(effectiveTicket)
            : ParkingCalculator.CalculateFee(effectiveTicket, duration);
        if (request.ExpectedTotalFee is { } expectedTotal && expectedTotal != totalFee)
        {
            throw new ArgumentException(
                $"Total tagihan berubah menjadi Rp {totalFee.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}. Muat ulang tiket sebelum membayar.");
        }
        var status = request.LostTicket ? "LOST_TICKET" : effectiveTicket.IsMember ? "MEMBER" : "PAID";

        var completed = await repository.CompleteCheckoutAsync(
            ticketId, request, cashierUserId, timeOut, duration, totalFee, penalty, status, sourceIp,
            effectiveTicket.VehicleTypeId, licensePlate, classOverride, cancellationToken);
        if (!completed)
        {
            previousCheckout = await repository.GetCompletedCheckoutAsync(request.IdempotencyKey, cancellationToken);
            if (previousCheckout is not null && previousCheckout.TicketId == ticketId)
            {
                return previousCheckout;
            }

            throw new InvalidOperationException("Tiket telah ditutup oleh proses kasir lain.");
        }

        return new CheckoutResponse(ticketId, duration, totalFee, penalty, status, timeOut);
    }

    public async Task VoidTicketAsync(
        string ticketId,
        VoidTicketRequest request,
        long actorUserId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
        {
            throw new ArgumentException("Nomor tiket wajib diisi.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 5 || reason.Length > 200)
        {
            throw new ArgumentException("Alasan void wajib diisi 5-200 karakter.");
        }

        var voided = await repository.VoidTicketAsync(
            ticketId.Trim(), reason, actorUserId, sourceIp, cancellationToken);
        if (!voided)
        {
            throw new InvalidOperationException("Tiket tidak ditemukan atau sudah diproses.");
        }
    }

    public Task<MemberVerificationResponse> VerifyMemberAsync(string rfidUid, CancellationToken cancellationToken) =>
        repository.VerifyMemberAsync(rfidUid, cancellationToken);

    private async Task<VehicleTypeDto> GetVehicleTypeAsync(long vehicleTypeId, CancellationToken cancellationToken)
    {
        var vehicleTypes = await adminService.GetVehicleTypesAsync(cancellationToken);
        return vehicleTypes.FirstOrDefault(type => type.Id == vehicleTypeId)
            ?? throw new ArgumentException("Jenis kendaraan tidak ditemukan.");
    }

    private static TicketData ApplyVehicleType(TicketData ticket, VehicleTypeDto vehicleType) =>
        ticket with
        {
            VehicleTypeId = vehicleType.Id,
            VehicleTypeName = vehicleType.TypeName,
            GracePeriodMinutes = vehicleType.GracePeriodMinutes,
            FirstHourRate = vehicleType.FirstHourRate,
            HourlyRate = vehicleType.HourlyRate,
            LostTicketFee = vehicleType.LostTicketFee,
            MaxDailyRate = vehicleType.MaxDailyRate
        };

    private static string? NormalizeEntryKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var normalized = idempotencyKey.Trim();
        if (normalized.Length > 100)
        {
            throw new ArgumentException("IdempotencyKey entry maksimal 100 karakter.");
        }

        return normalized;
    }

    private static string NormalizeOverrideReason(string? overrideReason)
    {
        if (string.IsNullOrWhiteSpace(overrideReason))
        {
            throw new ArgumentException("Alasan perubahan kelas tarif wajib diisi.");
        }

        var normalized = overrideReason.Trim();
        if (normalized.Length < 5)
        {
            throw new ArgumentException("Alasan perubahan kelas tarif minimal 5 karakter.");
        }
        if (normalized.Length > 200)
        {
            throw new ArgumentException("Alasan perubahan kelas tarif maksimal 200 karakter.");
        }

        return normalized;
    }

    private static string? NormalizeLicensePlate(string? licensePlate)
    {
        if (string.IsNullOrWhiteSpace(licensePlate))
        {
            return null;
        }

        var normalized = licensePlate.Trim().ToUpperInvariant();
        if (normalized.Length > 20)
        {
            throw new ArgumentException("Plat nomor maksimal 20 karakter.");
        }

        return normalized;
    }
}