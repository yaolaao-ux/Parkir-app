using Npgsql;
using Parkir.Api.Models;

namespace Parkir.Api.Data;

public sealed class ParkingRepository(NpgsqlDataSource dataSource)
{
    public async Task<string?> CreateEntryAsync(
        string ticketId,
        EntryRequest request,
        DateTimeOffset timeIn,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        long vehicleTypeId = request.VehicleTypeId;
        long? memberId = null;
        string? memberName = null;

        if (!string.IsNullOrWhiteSpace(request.RfidUid))
        {
            const string memberSql = """
                SELECT id, full_name, vehicle_type_id
                FROM members
                WHERE rfid_uid = @uid AND status = 'ACTIVE' AND expired_date >= CURRENT_DATE
                """;
            await using var memberCommand = new NpgsqlCommand(memberSql, connection);
            memberCommand.Parameters.AddWithValue("uid", request.RfidUid.Trim());
            await using var memberReader = await memberCommand.ExecuteReaderAsync(cancellationToken);
            if (!await memberReader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Kartu RFID tidak aktif atau masa berlakunya habis.");
            }

            memberId = memberReader.GetInt64(0);
            memberName = memberReader.GetString(1);
            if (!memberReader.IsDBNull(2))
            {
                vehicleTypeId = memberReader.GetInt64(2);
            }
        }

        const string insertSql = """
            INSERT INTO transactions
                (ticket_id, vehicle_type_id, member_id, gate_in_id, time_in, photo_in_path)
            VALUES
                (@ticket_id, @vehicle_type_id, @member_id, @gate_id, @time_in, @photo_path)
            """;
        await using var command = new NpgsqlCommand(insertSql, connection);
        command.Parameters.AddWithValue("ticket_id", ticketId);
        command.Parameters.AddWithValue("vehicle_type_id", vehicleTypeId);
        command.Parameters.AddWithValue("member_id", memberId is null ? DBNull.Value : memberId.Value);
        command.Parameters.AddWithValue("gate_id", request.GateId);
        command.Parameters.AddWithValue("time_in", timeIn);
        command.Parameters.AddWithValue("photo_path", (object?)request.PhotoInPath ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return memberName;
    }

    public async Task<TicketData> GetTicketAsync(string ticketId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT t.ticket_id, t.time_in, t.photo_in_path, t.payment_status,
                   (m.id IS NOT NULL AND m.status = 'ACTIVE' AND m.expired_date >= CURRENT_DATE),
                   m.full_name, v.grace_period_minutes, v.first_hour_rate, v.hourly_rate,
                   v.lost_ticket_fee, v.max_daily_rate
            FROM transactions t
            JOIN vehicle_types v ON v.id = t.vehicle_type_id
            LEFT JOIN members m ON m.id = t.member_id
            WHERE t.ticket_id = @ticket_id
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ticket_id", ticketId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new KeyNotFoundException("Tiket tidak ditemukan.");
        }

        return new TicketData(
            reader.GetString(0),
            new DateTimeOffset(reader.GetDateTime(1)),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt32(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetDecimal(9),
            reader.GetDecimal(10));
    }

    public async Task<bool> CompleteCheckoutAsync(
        string ticketId,
        CheckoutRequest request,
        long cashierUserId,
        DateTimeOffset timeOut,
        int durationMinutes,
        decimal totalFee,
        decimal penaltyFee,
        string paymentStatus,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE transactions
            SET gate_out_id = @gate_id,
                time_out = @time_out,
                duration_minutes = @duration,
                total_fee = @total_fee,
                penalty_fee = @penalty_fee,
                payment_status = @status,
                payment_method = @payment_method,
                cashier_user_id = @cashier_id,
                photo_out_path = @photo_path,
                checkout_id = @checkout_id
            WHERE ticket_id = @ticket_id AND time_out IS NULL AND payment_status = 'UNPAID'
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("gate_id", request.GateId);
        command.Parameters.AddWithValue("time_out", timeOut);
        command.Parameters.AddWithValue("duration", durationMinutes);
        command.Parameters.AddWithValue("total_fee", totalFee);
        command.Parameters.AddWithValue("penalty_fee", penaltyFee);
        command.Parameters.AddWithValue("status", paymentStatus);
        command.Parameters.AddWithValue("payment_method", paymentStatus == "MEMBER" ? DBNull.Value : request.PaymentMethod);
        command.Parameters.AddWithValue("cashier_id", cashierUserId);
        command.Parameters.AddWithValue("photo_path", (object?)request.PhotoOutPath ?? DBNull.Value);
        command.Parameters.AddWithValue("checkout_id", request.IdempotencyKey);
        command.Parameters.AddWithValue("ticket_id", ticketId);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (changed)
        {
            const string auditSql = """
                INSERT INTO audit_logs (actor_user_id, action, entity_type, entity_id, details, source_ip)
                VALUES (@actor_id, 'PARKING_CHECKOUT', 'TRANSACTION', @entity_id, @details::jsonb, @source_ip::inet)
                """;
            await using var auditCommand = new NpgsqlCommand(auditSql, connection, transaction);
            auditCommand.Parameters.AddWithValue("actor_id", cashierUserId);
            auditCommand.Parameters.AddWithValue("entity_id", ticketId);
            auditCommand.Parameters.AddWithValue("details", System.Text.Json.JsonSerializer.Serialize(new
            {
                gateId = request.GateId,
                paymentStatus,
                totalFee,
                penaltyFee,
                idempotencyKey = request.IdempotencyKey
            }));
            auditCommand.Parameters.AddWithValue("source_ip", (object?)sourceIp ?? DBNull.Value);
            await auditCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    public async Task<CheckoutResponse?> GetCompletedCheckoutAsync(
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ticket_id, duration_minutes, total_fee, penalty_fee, payment_status, time_out
            FROM transactions
            WHERE checkout_id = @checkout_id
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("checkout_id", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CheckoutResponse(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetDecimal(2),
            reader.GetDecimal(3),
            reader.GetString(4),
            new DateTimeOffset(reader.GetDateTime(5)));
    }

    public async Task<MemberVerificationResponse> VerifyMemberAsync(
        string rfidUid,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT full_name, license_plate, expired_date
            FROM members
            WHERE rfid_uid = @uid AND status = 'ACTIVE' AND expired_date >= CURRENT_DATE
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("uid", rfidUid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new MemberVerificationResponse(true, reader.GetString(0), reader.GetString(1), reader.GetFieldValue<DateOnly>(2))
            : new MemberVerificationResponse(false, null, null, null);
    }
}