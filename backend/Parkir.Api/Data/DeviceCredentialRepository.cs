using Npgsql;
using System.Text.Json;

namespace Parkir.Api.Data;

public sealed class DeviceCredentialRepository(NpgsqlDataSource dataSource)
{
    public async Task<string?> GetActiveHashAsync(string deviceId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT key_hash
            FROM device_credentials
            WHERE device_id = @device_id
              AND enabled
              AND revoked_at IS NULL
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("device_id", deviceId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<bool> IssueAsync(
        string deviceId,
        string keyHash,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var existingCommand = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM device_credentials WHERE device_id = @device_id)",
            connection,
            transaction);
        existingCommand.Parameters.AddWithValue("device_id", deviceId);
        var existed = (bool)(await existingCommand.ExecuteScalarAsync(cancellationToken) ?? false);

        const string sql = """
            INSERT INTO device_credentials (device_id, key_hash, enabled, rotated_at, revoked_at)
            VALUES (@device_id, @key_hash, TRUE, NULL, NULL)
            ON CONFLICT (device_id) DO UPDATE
            SET key_hash = EXCLUDED.key_hash,
                enabled = TRUE,
                rotated_at = CURRENT_TIMESTAMP,
                revoked_at = NULL
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("key_hash", keyHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await WriteAuditAsync(
            connection, transaction, actorId, existed ? "DEVICE_CREDENTIAL_ROTATED" : "DEVICE_CREDENTIAL_CREATED",
            "DEVICE", deviceId, new { deviceId, enabled = true }, sourceIp, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return existed;
    }

    public async Task<bool> RevokeAsync(
        string deviceId,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE device_credentials
            SET enabled = FALSE, revoked_at = CURRENT_TIMESTAMP
            WHERE device_id = @device_id AND revoked_at IS NULL
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("device_id", deviceId);
        var revoked = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (revoked)
        {
            await WriteAuditAsync(
                connection, transaction, actorId, "DEVICE_CREDENTIAL_REVOKED", "DEVICE", deviceId,
                new { deviceId, enabled = false }, sourceIp, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return revoked;
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long actorId,
        string action,
        string entityType,
        string entityId,
        object details,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO audit_logs (actor_user_id, action, entity_type, entity_id, details, source_ip)
            VALUES (@actor_id, @action, @entity_type, @entity_id, @details::jsonb, @source_ip::inet)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("actor_id", actorId);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("details", JsonSerializer.Serialize(details));
        command.Parameters.AddWithValue("source_ip", (object?)sourceIp ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
