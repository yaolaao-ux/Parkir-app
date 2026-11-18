using Npgsql;
using Parkir.Api.Models;
using Parkir.Api.Services;
using System.Text.Json;

namespace Parkir.Api.Data;

public sealed class GateRepository(NpgsqlDataSource dataSource)
{
    public async Task<bool> UpdatePrintStatusAsync(
        string ticketId,
        string gateId,
        string status,
        string? detail,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string readSql = """
            SELECT gate_in_id, print_status
            FROM transactions
            WHERE ticket_id = @ticket_id
            FOR UPDATE
            """;
        await using var readCommand = new NpgsqlCommand(readSql, connection, transaction);
        readCommand.Parameters.AddWithValue("ticket_id", ticketId);
        string ticketGate;
        string currentStatus;
        await using (var reader = await readCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new KeyNotFoundException("Tiket tidak ditemukan.");
            }

            ticketGate = reader.GetString(0);
            currentStatus = reader.GetString(1);
        }

        if (!string.Equals(ticketGate, gateId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Perangkat tidak berhak mengubah status cetak tiket gate lain.");
        }

        if (currentStatus == status)
        {
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        var allowed = TicketLifecycle.IsPrintTransitionAllowed(currentStatus, status);
        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Transisi status cetak {currentStatus} ke {status} tidak diizinkan.");
        }

        const string updateSql = """
            UPDATE transactions
            SET print_status = @status
            WHERE ticket_id = @ticket_id
            """;
        await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
        updateCommand.Parameters.AddWithValue("ticket_id", ticketId);
        updateCommand.Parameters.AddWithValue("status", status);
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        await WriteAuditAsync(
            connection, transaction, null, "TICKET_PRINT_STATUS_UPDATED", "TRANSACTION",
            ticketId, new { status, detail }, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<ReprintResult?> ReprintAsync(
        string ticketId,
        string reason,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE transactions
            SET reprint_count = reprint_count + 1,
                last_reprint_at = CURRENT_TIMESTAMP
            WHERE ticket_id = @ticket_id AND payment_status <> 'VOID'
            RETURNING reprint_count
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("ticket_id", ticketId);
        var count = await command.ExecuteScalarAsync(cancellationToken);
        if (count is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var reprintCount = (int)count;
        await WriteAuditAsync(
            connection, transaction, actorId, "TICKET_REPRINTED", "TRANSACTION",
            ticketId, new { reason, reprintCount }, sourceIp, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ReprintResult(ticketId, reprintCount);
    }

    public async Task RecordGateEventAsync(
        string ticketId,
        GateEventRequest request,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string ticketSql = """
            SELECT gate_out_id, payment_status
            FROM transactions
            WHERE ticket_id = @ticket_id
            FOR UPDATE
            """;
        await using var ticketCommand = new NpgsqlCommand(ticketSql, connection, transaction);
        ticketCommand.Parameters.AddWithValue("ticket_id", ticketId);
        string? gateOut;
        string paymentStatus;
        await using (var reader = await ticketCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new KeyNotFoundException("Tiket tidak ditemukan.");
            }

            gateOut = reader.IsDBNull(0) ? null : reader.GetString(0);
            paymentStatus = reader.GetString(1);
        }

        if (string.IsNullOrWhiteSpace(gateOut) ||
            !string.Equals(gateOut, request.GateId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Gate event tidak sesuai dengan gate keluar tiket.");
        }
        if (paymentStatus is "UNPAID" or "VOID")
        {
            throw new InvalidOperationException("Gate event hanya boleh untuk tiket yang sudah dibayar.");
        }

        const string existingSql = """
            SELECT ticket_id, gate_id, action, status
            FROM gate_commands
            WHERE command_id = @command_id
            FOR UPDATE
            """;
        await using var existingCommand = new NpgsqlCommand(existingSql, connection, transaction);
        existingCommand.Parameters.AddWithValue("command_id", request.CommandId);
        string? existingTicket = null;
        string? existingGate = null;
        string? existingAction = null;
        string? existingStatus = null;
        await using (var reader = await existingCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                existingTicket = reader.GetString(0);
                existingGate = reader.GetString(1);
                existingAction = reader.GetString(2);
                existingStatus = reader.GetString(3);
            }
        }

        if (existingTicket is not null)
        {
            if (!string.Equals(existingTicket, ticketId, StringComparison.Ordinal) ||
                !string.Equals(existingGate, request.GateId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(existingAction, request.Action, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Command ID sudah dipakai untuk gate/tiket/aksi lain.");
            }

            if (existingStatus is null)
            {
                throw new InvalidOperationException("Status command gate tidak terbaca.");
            }

            if (existingStatus == request.Status)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var allowed = TicketLifecycle.IsGateTransitionAllowed(existingStatus, request.Status);
            if (!allowed)
            {
                throw new InvalidOperationException(
                    $"Transisi command gate {existingStatus} ke {request.Status} tidak diizinkan.");
            }

            const string updateSql = """
                UPDATE gate_commands
                SET status = @status, detail = @detail, actor_user_id = @actor_id,
                    updated_at = CURRENT_TIMESTAMP
                WHERE command_id = @command_id
                """;
            await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
            updateCommand.Parameters.AddWithValue("command_id", request.CommandId);
            updateCommand.Parameters.AddWithValue("status", request.Status);
            updateCommand.Parameters.AddWithValue("detail", (object?)request.Detail ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("actor_id", actorId);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            const string insertSql = """
                INSERT INTO gate_commands
                    (command_id, ticket_id, gate_id, action, status, detail, actor_user_id)
                VALUES
                    (@command_id, @ticket_id, @gate_id, @action, @status, @detail, @actor_id)
                """;
            await using var insertCommand = new NpgsqlCommand(insertSql, connection, transaction);
            insertCommand.Parameters.AddWithValue("command_id", request.CommandId);
            insertCommand.Parameters.AddWithValue("ticket_id", ticketId);
            insertCommand.Parameters.AddWithValue("gate_id", request.GateId);
            insertCommand.Parameters.AddWithValue("action", request.Action);
            insertCommand.Parameters.AddWithValue("status", request.Status);
            insertCommand.Parameters.AddWithValue("detail", (object?)request.Detail ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("actor_id", actorId);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection, transaction, actorId, "GATE_COMMAND_UPDATED", "TRANSACTION",
            ticketId, new
            {
                commandId = request.CommandId,
                gateId = request.GateId,
                request.Action,
                request.Status,
                request.Detail
            }, sourceIp, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long? actorId,
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
        command.Parameters.AddWithValue("actor_id", actorId is null ? DBNull.Value : actorId.Value);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("details", JsonSerializer.Serialize(details));
        command.Parameters.AddWithValue("source_ip", (object?)sourceIp ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed record ReprintResult(string TicketId, int ReprintCount);
