using Npgsql;

namespace Parkir.Api.Data;

public sealed class AuditRepository(NpgsqlDataSource dataSource)
{
    public async Task WriteAsync(
        long? actorUserId,
        string action,
        string entityType,
        string? entityId,
        string detailsJson,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO audit_logs (actor_user_id, action, entity_type, entity_id, details, source_ip)
            VALUES (@actor_id, @action, @entity_type, @entity_id, @details::jsonb, @source_ip::inet)
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("actor_id", actorUserId is null ? DBNull.Value : actorUserId.Value);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", (object?)entityId ?? DBNull.Value);
        command.Parameters.AddWithValue("details", detailsJson);
        command.Parameters.AddWithValue("source_ip", (object?)sourceIp ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}