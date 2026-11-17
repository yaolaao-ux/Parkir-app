using System.Globalization;

namespace Parkir.Api.Services;

public sealed class BusinessClock
{
    private readonly TimeZoneInfo _timeZone;

    public BusinessClock(IConfiguration configuration)
    {
        TimeZoneId = configuration["Business:TimeZone"]?.Trim() is { Length: > 0 } configured
            ? configured
            : "Asia/Jakarta";
        try
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"Business:TimeZone '{TimeZoneId}' tidak ditemukan pada server.", exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new InvalidOperationException(
                $"Business:TimeZone '{TimeZoneId}' tidak valid pada server.", exception);
        }
    }

    public string TimeZoneId { get; }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(UtcNow, _timeZone);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public DateTimeOffset DayStartUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
            _timeZone);

    public DateTimeOffset DayEndUtc(DateOnly date) => DayStartUtc(date.AddDays(1));

    public string FileDate => Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
