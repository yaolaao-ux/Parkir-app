using Npgsql;
using Parkir.Api.Models;

namespace Parkir.Api.Data;

public sealed class AdminRepository(NpgsqlDataSource dataSource)
{
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
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH report_days AS (
                SELECT generate_series(@from::date, @to::date, INTERVAL '1 day')::date AS report_date
            )
            SELECT d.report_date,
                   count(t.ticket_id) FILTER (
                       WHERE t.time_in >= d.report_date::timestamptz
                         AND t.time_in < (d.report_date + 1)::timestamptz
                   ),
                   count(t.ticket_id) FILTER (
                       WHERE t.time_out >= d.report_date::timestamptz
                         AND t.time_out < (d.report_date + 1)::timestamptz
                   ),
                   coalesce(sum(t.total_fee) FILTER (
                       WHERE t.time_out >= d.report_date::timestamptz
                         AND t.time_out < (d.report_date + 1)::timestamptz
                         AND t.payment_status IN ('PAID', 'LOST_TICKET')
                   ), 0)
            FROM report_days d
            LEFT JOIN transactions t ON (
                (t.time_in >= d.report_date::timestamptz AND t.time_in < (d.report_date + 1)::timestamptz)
                OR (t.time_out >= d.report_date::timestamptz AND t.time_out < (d.report_date + 1)::timestamptz)
            )
            GROUP BY d.report_date
            ORDER BY d.report_date
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var days = new List<DailyReportRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            days.Add(new DailyReportRow(
                reader.GetFieldValue<DateOnly>(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetDecimal(3)));
        }

        return days;
    }

    public async Task<DashboardSummary> GetDashboardAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                count(*) FILTER (WHERE time_out IS NULL),
                count(*) FILTER (WHERE time_in >= CURRENT_DATE AND time_in < CURRENT_DATE + INTERVAL '1 day'),
                count(*) FILTER (WHERE time_out >= CURRENT_DATE AND time_out < CURRENT_DATE + INTERVAL '1 day'),
                coalesce(sum(total_fee) FILTER (
                    WHERE time_out >= CURRENT_DATE AND time_out < CURRENT_DATE + INTERVAL '1 day'
                      AND payment_status IN ('PAID', 'LOST_TICKET')
                ), 0)
            FROM transactions
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
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

    public async Task<long> CreateVehicleTypeAsync(VehicleTypeInput input, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO vehicle_types
                (type_name, first_hour_rate, hourly_rate, grace_period_minutes, lost_ticket_fee, max_daily_rate)
            VALUES (@name, @first, @hourly, @grace, @lost, @daily)
            RETURNING id
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddVehicleTypeParameters(command, input);
        return (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Jenis kendaraan gagal dibuat."));
    }

    public async Task<bool> UpdateVehicleTypeAsync(long id, VehicleTypeInput input, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE vehicle_types
            SET type_name = @name, first_hour_rate = @first, hourly_rate = @hourly,
                grace_period_minutes = @grace, lost_ticket_fee = @lost, max_daily_rate = @daily
            WHERE id = @id
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddVehicleTypeParameters(command, input);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
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

    public async Task<long> CreateMemberAsync(MemberInput input, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO members (rfid_uid, full_name, license_plate, vehicle_type_id, expired_date)
            VALUES (@uid, @name, @plate, @vehicle_type_id, @expired)
            RETURNING id
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("uid", input.RfidUid.Trim());
        command.Parameters.AddWithValue("name", input.FullName.Trim());
        command.Parameters.AddWithValue("plate", input.LicensePlate.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("vehicle_type_id", input.VehicleTypeId is null ? DBNull.Value : input.VehicleTypeId.Value);
        command.Parameters.AddWithValue("expired", input.ExpiredDate);
        return (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Member gagal dibuat."));
    }

    public async Task<bool> UpdateMemberStatusAsync(long id, string status, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE members SET status = @status WHERE id = @id";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
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
}