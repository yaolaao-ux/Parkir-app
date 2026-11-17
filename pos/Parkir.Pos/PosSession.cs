namespace Parkir.Pos;

public sealed record PosSession(
    string AccessToken,
    string Username,
    long UserId,
    string FullName,
    string Role,
    DateTimeOffset ExpiresAt);