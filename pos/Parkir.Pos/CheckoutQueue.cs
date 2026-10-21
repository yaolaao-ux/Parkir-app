using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.Json;

namespace Parkir.Pos;

internal sealed record PendingCheckout(
    Guid IdempotencyKey,
    string TicketId,
    string Payload,
    DateTimeOffset CreatedAt);

internal sealed class CheckoutQueue
{
    private readonly string _connectionString;

    public CheckoutQueue()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Parkir", "Pos");
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "checkout-outbox.db"),
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS pending_checkouts (
                idempotency_key TEXT PRIMARY KEY,
                ticket_id TEXT NOT NULL,
                payload TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Enqueue(PendingCheckout item)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO pending_checkouts (idempotency_key, ticket_id, payload, created_at)
            VALUES ($key, $ticket, $payload, $created_at)
            """;
        command.Parameters.AddWithValue("$key", item.IdempotencyKey.ToString("D"));
        command.Parameters.AddWithValue("$ticket", item.TicketId);
        command.Parameters.AddWithValue("$payload", item.Payload);
        command.Parameters.AddWithValue("$created_at", item.CreatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<PendingCheckout> GetPending()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT idempotency_key, ticket_id, payload, created_at
            FROM pending_checkouts ORDER BY created_at
            """;
        using var reader = command.ExecuteReader();
        var items = new List<PendingCheckout>();
        while (reader.Read())
        {
            items.Add(new PendingCheckout(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3))));
        }

        return items;
    }

    public void Remove(Guid idempotencyKey)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM pending_checkouts WHERE idempotency_key = $key";
        command.Parameters.AddWithValue("$key", idempotencyKey.ToString("D"));
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}