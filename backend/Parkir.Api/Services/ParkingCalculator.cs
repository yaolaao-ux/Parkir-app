using Parkir.Api.Models;

namespace Parkir.Api.Services;

public static class ParkingCalculator
{
    public static int GetDurationMinutes(DateTimeOffset timeIn, DateTimeOffset timeOut) =>
        Math.Max(0, (int)Math.Ceiling((timeOut - timeIn).TotalMinutes));

    public static decimal CalculateLostTicketTotal(TicketData ticket) =>
        ticket.LostTicketFee + ticket.MaxDailyRate;

    public static decimal CalculateFee(TicketData ticket, int durationMinutes)
    {
        if (ticket.IsMember || durationMinutes <= ticket.GracePeriodMinutes)
        {
            return 0;
        }

        var hours = (int)Math.Ceiling(durationMinutes / 60m);
        var regularFee = hours <= 1
            ? ticket.FirstHourRate
            : ticket.FirstHourRate + (hours - 1) * ticket.HourlyRate;
        if (ticket.MaxDailyRate <= 0)
        {
            return regularFee;
        }

        var chargedDays = (int)Math.Ceiling(durationMinutes / 1440m);
        return Math.Min(regularFee, chargedDays * ticket.MaxDailyRate);
    }
}
