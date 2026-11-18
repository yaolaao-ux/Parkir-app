namespace Parkir.Api.Services;

public static class TicketLifecycle
{
    public static bool IsPrintTransitionAllowed(string current, string next) =>
        (current, next) switch
        {
            ("PENDING", "PRINT_SENT") => true,
            ("PENDING", "PRINT_FAILED") => true,
            ("PRINT_SENT", "PRINT_CONFIRMED") => true,
            ("PRINT_SENT", "PRINT_FAILED") => true,
            ("PRINT_FAILED", "PRINT_SENT") => true,
            _ => false
        };

    public static bool IsGateTransitionAllowed(string current, string next) =>
        (current, next) switch
        {
            ("SENT", "ACKNOWLEDGED") => true,
            ("SENT", "EXECUTED") => true,
            ("SENT", "FAILED") => true,
            ("ACKNOWLEDGED", "EXECUTED") => true,
            ("ACKNOWLEDGED", "FAILED") => true,
            _ => false
        };
}
