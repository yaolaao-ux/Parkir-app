namespace Parkir.Pos;

public sealed record PosSession(
    string AccessToken,
    long UserId,
    string FullName,
    string Role,
    DateTimeOffset ExpiresAt);