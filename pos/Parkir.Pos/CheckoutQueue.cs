using Microsoft.Data.Sqlite;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Parkir.Pos;

internal sealed record PendingCheckout(
    Guid IdempotencyKey,
    string TicketId,
    string Payload,
    DateTimeOffset CreatedAt,
    string Status,
    int AttemptCount,
    string? LastError,
    string? ResultPayload,
    bool HardwareDone);

internal sealed class CheckoutQueue
{
    private readonly string _connectionString;
    private readonly string _logPath;
    private readonly string _databasePath;

    public CheckoutQueue(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Parkir", "Pos");
        Directory.CreateDirectory(directory);
        _databasePath = Path.Combine(directory, "checkout-outbox.db");
        _logPath = Path.Combine(directory, "outbox.log");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5
        }.ToString();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS pending_checkouts (
                idempotency_key TEXT PRIMARY KEY,
                ticket_id TEXT NOT NULL,
                payload TEXT NOT NULL,
                created_at TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'PENDING',
                attempt_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT,
                next_attempt_at TEXT,
                result_payload TEXT,
                hardware_done INTEGER NOT NULL DEFAULT 0
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumns(connection);
        RestrictFilePermissions(_databasePath);
    }

    public void Enqueue(PendingCheckout item)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO pending_checkouts
                (idempotency_key, ticket_id, payload, created_at, status, attempt_count,
                 last_error, next_attempt_at, result_payload, hardware_done)
            VALUES ($key, $ticket, $payload, $created_at, 'PENDING', 0,
                    NULL, $created_at, NULL, 0)
            """;
        command.Parameters.AddWithValue("$key", item.IdempotencyKey.ToString("D"));
        command.Parameters.AddWithValue("$ticket", item.TicketId);
        command.Parameters.AddWithValue("$payload", item.Payload);
        command.Parameters.AddWithValue("$created_at", item.CreatedAt.ToString("O"));
        command.ExecuteNonQuery();
        Log($"ENQUEUE ticket={item.TicketId} key={item.IdempotencyKey:D}");
    }

    public IReadOnlyList<PendingCheckout> GetWorkItems(DateTimeOffset now)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT idempotency_key, ticket_id, payload, created_at, status, attempt_count,
                   last_error, result_payload, hardware_done
            FROM pending_checkouts
            WHERE (status IN ('PENDING', 'SENT') AND COALESCE(next_attempt_at, created_at) <= $now)
               OR (status = 'CONFIRMED' AND hardware_done = 0)
            ORDER BY created_at
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        return ReadItems(command);
    }

    public IReadOnlyList<PendingCheckout> GetFailed()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT idempotency_key, ticket_id, payload, created_at, status, attempt_count,
                   last_error, result_payload, hardware_done
            FROM pending_checkouts
            WHERE status = 'FAILED'
            ORDER BY created_at
            """;
        return ReadItems(command);
    }

    public int GetOutstandingCount()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM pending_checkouts
            WHERE status IN ('PENDING', 'SENT', 'FAILED', 'CONFIRMED')
              AND NOT (status = 'CONFIRMED' AND hardware_done = 1)
            """;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int GetFailedCount()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM pending_checkouts WHERE status = 'FAILED'";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void MarkRetry(Guid idempotencyKey, string error, TimeSpan delay)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pending_checkouts
            SET status = 'SENT',
                attempt_count = attempt_count + 1,
                last_error = $error,
                next_attempt_at = $next_attempt
            WHERE idempotency_key = $key AND status IN ('PENDING', 'SENT')
            """;
        command.Parameters.AddWithValue("$key", idempotencyKey.ToString("D"));
        command.Parameters.AddWithValue("$error", error);
        command.Parameters.AddWithValue("$next_attempt", DateTimeOffset.UtcNow.Add(delay).ToString("O"));
        command.ExecuteNonQuery();
        Log($"RETRY ticket_key={idempotencyKey:D} delay={delay.TotalSeconds:0}s error={TrimError(error)}");
    }

    public void MarkConfirmed(Guid idempotencyKey, string resultPayload)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pending_checkouts
            SET status = 'CONFIRMED',
                result_payload = $result,
                last_error = NULL,
                next_attempt_at = NULL
            WHERE idempotency_key = $key
            """;
        command.Parameters.AddWithValue("$key", idempotencyKey.ToString("D"));
        command.Parameters.AddWithValue("$result", resultPayload);
        command.ExecuteNonQuery();
        Log($"CONFIRMED ticket_key={idempotencyKey:D}");
    }

    public void MarkFailed(Guid idempotencyKey, string error)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pending_checkouts
            SET status = 'FAILED',
                last_error = $error,
                next_attempt_at = NULL
            WHERE idempotency_key = $key
            """;
        command.Parameters.AddWithValue("$key", idempotencyKey.ToString("D"));
        command.Parameters.AddWithValue("$error", error);
        command.ExecuteNonQuery();
        Log($"FAILED ticket_key={idempotencyKey:D} error={TrimError(error)}");
    }

    public void MarkHardwareDone(Guid idempotencyKey)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pending_checkouts
            SET status = 'HARDWARE_DONE', hardware_done = 1
            WHERE idempotency_key = $key
            """;
        command.Parameters.AddWithValue("$key", idempotencyKey.ToString("D"));
        command.ExecuteNonQuery();
        Log($"HARDWARE_DONE ticket_key={idempotencyKey:D}");
    }

    public void RetryFailed()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pending_checkouts
            SET status = 'PENDING',
                attempt_count = 0,
                last_error = NULL,
                next_attempt_at = $now
            WHERE status = 'FAILED'
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        Log("RETRY_FAILED_ALL");
    }

    public void Remove(Guid idempotencyKey)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM pending_checkouts WHERE idempotency_key = $key";
        command.Parameters.AddWithValue("$key", idempotencyKey.ToString("D"));
        command.ExecuteNonQuery();
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(_logPath, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
            RestrictFilePermissions(_logPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string TrimError(string error)
    {
        var normalized = error.ReplaceLineEndings(" ");
        return normalized.Length <= 120 ? normalized : normalized[..120];
    }

    private static void RestrictFilePermissions(string filePath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            var identity = WindowsIdentity.GetCurrent().User;
            if (identity is null)
            {
                return;
            }

            var info = new FileInfo(filePath);
            var security = info.GetAccessControl();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                identity, FileSystemRights.FullControl, AccessControlType.Allow));
            info.SetAccessControl(security);
        }
        catch (Exception)
        {
            // ACL bersifat best-effort; jangan hentikan antrean checkout karena kebijakan filesystem.
        }
    }

    private static IReadOnlyList<PendingCheckout> ReadItems(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var items = new List<PendingCheckout>();
        while (reader.Read())
        {
            items.Add(new PendingCheckout(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetInt64(8) == 1));
        }

        return items;
    }

    private static void EnsureColumns(SqliteConnection connection)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(pending_checkouts)";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetString(1));
            }
        }

        var columns = new (string Name, string Definition)[]
        {
            ("status", "TEXT NOT NULL DEFAULT 'PENDING'"),
            ("attempt_count", "INTEGER NOT NULL DEFAULT 0"),
            ("last_error", "TEXT"),
            ("next_attempt_at", "TEXT"),
            ("result_payload", "TEXT"),
            ("hardware_done", "INTEGER NOT NULL DEFAULT 0")
        };
        foreach (var (name, definition) in columns)
        {
            if (existing.Contains(name))
            {
                continue;
            }

            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE pending_checkouts ADD COLUMN {name} {definition}";
            alter.ExecuteNonQuery();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
