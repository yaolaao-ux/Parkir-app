using Npgsql;
using Parkir.Api.Models;
using System.Text.Json;

namespace Parkir.Api.Data;

public sealed class AdminRepository(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<GateDto>> GetGatesAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT g.gate_id, g.gate_type, g.display_name, g.enabled,
                   coalesce(d.last_seen >= CURRENT_TIMESTAMP - INTERVAL '60 seconds', FALSE), d.last_seen
            FROM gates g
            LEFT JOIN devices d ON d.device_id = g.gate_id
                AND d.device_type = CASE WHEN g.gate_type = 'GATE_IN' THEN 'GATE_IN' ELSE 'CASHIER_POS' END
            ORDER BY g.gate_type, g.gate_id
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var gates = new List<GateDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            gates.Add(new GateDto(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3),
                reader.GetBoolean(4), reader.IsDBNull(5) ? null : new DateTimeOffset(reader.GetDateTime(5))));
        }

        return gates;
    }

    public async Task<bool> IsGateEnabledAsync(string gateId, string gateType, CancellationToken cancellationToken)
    {
        const string sql = "SELECT EXISTS (SELECT 1 FROM gates WHERE gate_id = @gate_id AND gate_type = @gate_type AND enabled)";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("gate_id", gateId.Trim());
        command.Parameters.AddWithValue("gate_type", gateType);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> CreateGateAsync(
        GateInput input,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO gates (gate_id, gate_type, display_name, enabled)
            VALUES (@gate_id, @gate_type, @display_name, @enabled)
            ON CONFLICT (gate_id) DO NOTHING
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddGateParameters(command, input);
        var created = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (created)
        {
            await WriteAuditAsync(
                connection, transaction, actorId, "GATE_CREATED", "GATE",
                input.GateId.Trim().ToUpperInvariant(), input, sourceIp, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<bool> UpdateGateAsync(
        string gateId,
        GateInput input,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE gates SET gate_type = @gate_type, display_name = @display_name, enabled = @enabled
            WHERE gate_id = @gate_id
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddGateParameters(command, input);
        command.Parameters.AddWithValue("gate_id", gateId);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (updated)
        {
            await WriteAuditAsync(
                connection, transaction, actorId, "GATE_UPDATED", "GATE",
                gateId, input, sourceIp, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    private static void AddGateParameters(NpgsqlCommand command, GateInput input)
    {
        command.Parameters.AddWithValue("gate_id", input.GateId.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("gate_type", input.GateType);
        command.Parameters.AddWithValue("display_name", input.DisplayName.Trim());
        command.Parameters.AddWithValue("enabled", input.Enabled);
    }

    public async Task RecordDeviceHeartbeatAsync(
        DeviceHeartbeatRequest request,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO devices (device_id, device_type, display_name, firmware_version, ip_address, last_seen)
            VALUES (@device_id, @device_type, @display_name, @firmware_version, @source_ip::inet, CURRENT_TIMESTAMP)
            ON CONFLICT (device_id) DO UPDATE
            SET device_type = EXCLUDED.device_type,
                display_name = EXCLUDED.display_name,
                firmware_version = EXCLUDED.firmware_version,
                ip_address = EXCLUDED.ip_address,
                last_seen = CURRENT_TIMESTAMP
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("device_id", request.DeviceId.Trim());
        command.Parameters.AddWithValue("device_type", request.DeviceType);
        command.Parameters.AddWithValue("display_name", request.DisplayName.Trim());
        command.Parameters.AddWithValue("firmware_version", (object?)request.FirmwareVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("source_ip", (object?)sourceIp ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceStatusDto>> GetDeviceStatusesAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT device_id, device_type, display_name, firmware_version, ip_address::text,
                   last_seen, last_seen >= CURRENT_TIMESTAMP - INTERVAL '60 seconds'
            FROM devices
            ORDER BY device_type, display_name
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var devices = new List<DeviceStatusDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            devices.Add(new DeviceStatusDto(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                new DateTimeOffset(reader.GetDateTime(5)),
                reader.GetBoolean(6)));
        }

        return devices;
    }

    public async Task<IReadOnlyList<DailyReportRow>> GetReportAsync(
        DateOnly from,
        DateOnly to,
        string timeZoneId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH report_days AS (
                SELECT generate_series(@from::date, @to::date, INTERVAL '1 day')::date AS report_date
            )
            SELECT d.report_date,
                   count(t.ticket_id) FILTER (
                       WHERE t.time_in >= (d.report_date::timestamp AT TIME ZONE @time_zone)
                         AND t.time_in < ((d.report_date + 1)::timestamp AT TIME ZONE @time_zone)
                         AND t.payment_status <> 'VOID'
                   ),
                   count(t.ticket_id) FILTER (
                       WHERE t.time_out >= (d.report_date::timestamp AT TIME ZONE @time_zone)
                         AND t.time_out < ((d.report_date + 1)::timestamp AT TIME ZONE @time_zone)
                         AND t.payment_status <> 'VOID'
                   ),
                   coalesce(sum(t.total_fee) FILTER (
                       WHERE t.time_out >= (d.report_date::timestamp AT TIME ZONE @time_zone)
                         AND t.time_out < ((d.report_date + 1)::timestamp AT TIME ZONE @time_zone)
                         AND t.payment_status IN ('PAID', 'LOST_TICKET')
                   ), 0),
                   count(t.ticket_id) FILTER (
                       WHERE t.time_out >= (d.report_date::timestamp AT TIME ZONE @time_zone)
                         AND t.time_out < ((d.report_date + 1)::timestamp AT TIME ZONE @time_zone)
                         AND t.payment_status IN ('PAID', 'MEMBER', 'LOST_TICKET')
                   ),
                   count(t.ticket_id) FILTER (
                       WHERE t.time_out >= (d.report_date::timestamp AT TIME ZONE @time_zone)
                         AND t.time_out < ((d.report_date + 1)::timestamp AT TIME ZONE @time_zone)
                         AND t.payment_status = 'VOID'
                   ),
                   count(t.ticket_id) FILTER (
                       WHERE t.time_in >= (d.report_date::timestamp AT TIME ZONE @time_zone)
                         AND t.time_in < ((d.report_date + 1)::timestamp AT TIME ZONE @time_zone)
                         AND t.payment_status = 'UNPAID'
                   )
            FROM report_days d
            LEFT JOIN transactions t ON (
                (t.time_in >= (d.report_date::timestamp AT TIME ZONE @time_zone)
                    AND t.time_in < ((d.report_date + 1)::timestamp AT TIME ZONE @time_zone))
                OR (t.time_out >= (d.report_date::timestamp AT TIME ZONE @time_zone)
                    AND t.time_out < ((d.report_date + 1)::timestamp AT TIME ZONE @time_zone))
            )
            GROUP BY d.report_date
            ORDER BY d.report_date
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        command.Parameters.AddWithValue("time_zone", timeZoneId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var days = new List<DailyReportRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            days.Add(new DailyReportRow(
                reader.GetFieldValue<DateOnly>(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetDecimal(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6)));
        }

        return days;
    }

    public async Task<DashboardSummary> GetDashboardAsync(
        DateTimeOffset dayStartUtc,
        DateTimeOffset dayEndUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                count(*) FILTER (WHERE time_out IS NULL),
                count(*) FILTER (WHERE time_in >= @day_start AND time_in < @day_end
                    AND payment_status <> 'VOID'),
                count(*) FILTER (WHERE time_out >= @day_start AND time_out < @day_end
                    AND payment_status <> 'VOID'),
                coalesce(sum(total_fee) FILTER (
                    WHERE time_out >= @day_start AND time_out < @day_end
                      AND payment_status IN ('PAID', 'LOST_TICKET')
                ), 0)
            FROM transactions
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("day_start", dayStartUtc);
        command.Parameters.AddWithValue("day_end", dayEndUtc);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new DashboardSummary(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetDecimal(3));
    }

    public async Task<IReadOnlyList<VehicleTypeDto>> GetVehicleTypesAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, type_name, first_hour_rate, hourly_rate, grace_period_minutes, lost_ticket_fee, max_daily_rate
            FROM vehicle_types ORDER BY type_name
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<VehicleTypeDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new VehicleTypeDto(
                reader.GetInt64(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3),
                reader.GetInt32(4), reader.GetDecimal(5), reader.GetDecimal(6)));
        }

        return items;
    }

    public async Task<long> CreateVehicleTypeAsync(
        VehicleTypeInput input,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO vehicle_types
                (type_name, first_hour_rate, hourly_rate, grace_period_minutes, lost_ticket_fee, max_daily_rate)
            VALUES (@name, @first, @hourly, @grace, @lost, @daily)
            RETURNING id
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddVehicleTypeParameters(command, input);
        var id = (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Jenis kendaraan gagal dibuat."));
        await WriteAuditAsync(
            connection, transaction, actorId, "VEHICLE_TYPE_CREATED", "VEHICLE_TYPE",
            id.ToString(), input, sourceIp, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<bool> UpdateVehicleTypeAsync(
        long id,
        VehicleTypeInput input,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE vehicle_types
            SET type_name = @name, first_hour_rate = @first, hourly_rate = @hourly,
                grace_period_minutes = @grace, lost_ticket_fee = @lost, max_daily_rate = @daily
            WHERE id = @id
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddVehicleTypeParameters(command, input);
        command.Parameters.AddWithValue("id", id);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (updated)
        {
            await WriteAuditAsync(
                connection, transaction, actorId, "VEHICLE_TYPE_UPDATED", "VEHICLE_TYPE",
                id.ToString(), input, sourceIp, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<IReadOnlyList<MemberDto>> GetMembersAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT m.id, m.rfid_uid, m.full_name, m.license_plate, m.vehicle_type_id,
                   v.type_name, m.expired_date, m.status
            FROM members m
            LEFT JOIN vehicle_types v ON v.id = m.vehicle_type_id
            ORDER BY m.full_name
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<MemberDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new MemberDto(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetFieldValue<DateOnly>(6), reader.GetString(7)));
        }

        return items;
    }

    public async Task<long> CreateMemberAsync(
        MemberInput input,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO members (rfid_uid, full_name, license_plate, vehicle_type_id, expired_date)
            VALUES (@uid, @name, @plate, @vehicle_type_id, @expired)
            RETURNING id
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("uid", input.RfidUid.Trim());
        command.Parameters.AddWithValue("name", input.FullName.Trim());
        command.Parameters.AddWithValue("plate", input.LicensePlate.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("vehicle_type_id", input.VehicleTypeId is null ? DBNull.Value : input.VehicleTypeId.Value);
        command.Parameters.AddWithValue("expired", input.ExpiredDate);
        var id = (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Member gagal dibuat."));
        await WriteAuditAsync(
            connection, transaction, actorId, "MEMBER_CREATED", "MEMBER",
            id.ToString(), input, sourceIp, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<bool> UpdateMemberAsync(
        long id,
        MemberUpdateInput input,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE members
            SET full_name = @name, license_plate = @plate,
                vehicle_type_id = @vehicle_type_id, expired_date = @expired
            WHERE id = @id
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", input.FullName.Trim());
        command.Parameters.AddWithValue("plate", input.LicensePlate.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("vehicle_type_id", input.VehicleTypeId is null ? DBNull.Value : input.VehicleTypeId.Value);
        command.Parameters.AddWithValue("expired", input.ExpiredDate);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (updated)
        {
            await WriteAuditAsync(
                connection, transaction, actorId, "MEMBER_UPDATED", "MEMBER",
                id.ToString(), input, sourceIp, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<bool> UpdateMemberStatusAsync(
        long id,
        string status,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = "UPDATE members SET status = @status WHERE id = @id";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("id", id);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (updated)
        {
            await WriteAuditAsync(
                connection, transaction, actorId, "MEMBER_STATUS_UPDATED", "MEMBER",
                id.ToString(), new { status }, sourceIp, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<bool> SetUserStatusAsync(
        string username,
        bool isActive,
        long actorId,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = "UPDATE users SET is_active = @is_active WHERE username = @username";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.AddWithValue("username", username);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (updated)
        {
            await WriteAuditAsync(
                connection, transaction, actorId, "USER_STATUS_UPDATED", "USER",
                username, new { isActive }, sourceIp, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    private static void AddVehicleTypeParameters(NpgsqlCommand command, VehicleTypeInput input)
    {
        command.Parameters.AddWithValue("name", input.TypeName.Trim());
        command.Parameters.AddWithValue("first", input.FirstHourRate);
        command.Parameters.AddWithValue("hourly", input.HourlyRate);
        command.Parameters.AddWithValue("grace", input.GracePeriodMinutes);
        command.Parameters.AddWithValue("lost", input.LostTicketFee);
        command.Parameters.AddWithValue("daily", input.MaxDailyRate);
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