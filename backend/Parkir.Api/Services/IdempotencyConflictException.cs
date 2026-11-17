namespace Parkir.Api.Services;

public sealed class IdempotencyConflictException(string message) : Exception(message);
