using Parkir.Api.Data;
using Parkir.Api.Models;

namespace Parkir.Api.Services;

public sealed class ParkingService(ParkingRepository repository)
{
    public async Task<EntryResponse> CreateEntryAsync(EntryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.GateId) || request.VehicleTypeId <= 0)
        {
            throw new ArgumentException("GateId dan VehicleTypeId wajib diisi.");
        }

        var timeIn = DateTimeOffset.UtcNow;
        var ticketId = $"TKT-{timeIn:yyyyMMdd}-{Guid.NewGuid():N}"[..21];
        var memberName = await repository.CreateEntryAsync(ticketId, request, timeIn, cancellationToken);
        return new EntryResponse(ticketId, timeIn, memberName);
    }

    public async Task<QuoteResponse> GetQuoteAsync(string ticketId, CancellationToken cancellationToken)
    {
        var ticket = await repository.GetTicketAsync(ticketId, cancellationToken);
        if (ticket.PaymentStatus != "UNPAID")
        {
            throw new InvalidOperationException("Tiket sudah diproses dan tidak dapat digunakan kembali.");
        }

        var duration = GetDurationMinutes(ticket.TimeIn, DateTimeOffset.UtcNow);
        var fee = CalculateFee(ticket, duration);
        return new QuoteResponse(
            ticket.TicketId,
            ticket.TimeIn,
            duration,
            fee,
            ticket.LostTicketFee + ticket.MaxDailyRate,
            ticket.PhotoInPath,
            ticket.IsMember,
            ticket.PaymentStatus);
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

        if (!request.LostTicket && string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            throw new ArgumentException("PaymentMethod wajib diisi.");
        }

        if (request.LostTicket && !request.IdentityVerified)
        {
            throw new ArgumentException("Verifikasi identitas wajib untuk karcis hilang.");
        }

        var timeOut = DateTimeOffset.UtcNow;
        var ticket = await repository.GetTicketAsync(ticketId, cancellationToken);
        if (ticket.PaymentStatus != "UNPAID")
        {
            throw new InvalidOperationException("Tiket sudah diproses dan tidak dapat digunakan kembali.");
        }

        var duration = GetDurationMinutes(ticket.TimeIn, timeOut);
        var penalty = request.LostTicket ? ticket.LostTicketFee : 0m;
        var totalFee = request.LostTicket
            ? penalty + ticket.MaxDailyRate
            : CalculateFee(ticket, duration);
        var status = request.LostTicket ? "LOST_TICKET" : ticket.IsMember ? "MEMBER" : "PAID";

        var completed = await repository.CompleteCheckoutAsync(
            ticketId, request, cashierUserId, timeOut, duration, totalFee, penalty, status, sourceIp, cancellationToken);
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

    public Task<MemberVerificationResponse> VerifyMemberAsync(string rfidUid, CancellationToken cancellationToken) =>
        repository.VerifyMemberAsync(rfidUid, cancellationToken);

    private static int GetDurationMinutes(DateTimeOffset timeIn, DateTimeOffset timeOut) =>
        Math.Max(0, (int)Math.Ceiling((timeOut - timeIn).TotalMinutes));

    private static decimal CalculateFee(TicketData ticket, int durationMinutes)
    {
        if (ticket.IsMember || durationMinutes <= ticket.GracePeriodMinutes)
        {
            return 0;
        }

        var hours = (int)Math.Ceiling(durationMinutes / 60m);
        return hours <= 1
            ? ticket.FirstHourRate
            : ticket.FirstHourRate + (hours - 1) * ticket.HourlyRate;
    }
}