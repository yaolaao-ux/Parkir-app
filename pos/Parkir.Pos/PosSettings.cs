namespace Parkir.Pos;

internal static class PosSettings
{
    public static string? DeviceKey
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("PARKIR_DEVICE_KEY");
            return string.IsNullOrWhiteSpace(configured)
                ? Environment.GetEnvironmentVariable("PARKIR_API_KEY")
                : configured;
        }
    }

    public static string DeviceId =>
        (Environment.GetEnvironmentVariable("PARKIR_DEVICE_ID") ??
            Environment.GetEnvironmentVariable("PARKIR_GATE_ID") ??
            "GATE-OUT-01").Trim().ToUpperInvariant();

    public static string GateId =>
        (Environment.GetEnvironmentVariable("PARKIR_GATE_ID") ??
            Environment.GetEnvironmentVariable("PARKIR_DEVICE_ID") ??
            "GATE-OUT-01").Trim().ToUpperInvariant();
}
