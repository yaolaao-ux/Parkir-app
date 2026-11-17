using System.Globalization;

namespace Parkir.Api.Services;

public static class TicketIdGenerator
{
    public static string Create(DateTimeOffset businessTime) =>
        $"TKT-{businessTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}";
}
