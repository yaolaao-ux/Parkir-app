using System.Reflection;
using Npgsql;

namespace Parkir.Api.Data;

public sealed class DatabaseInitializer(
    NpgsqlDataSource dataSource,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger)
    : IHostedService
{
    private const long AdvisoryLockKey = 746352;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Database:AutoMigrate", true))
        {
            logger.LogWarning("Database:AutoMigrate=false; migration otomatis dilewati.");
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection))
        {
            lockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await WaitForBaseSchemaAsync(connection, cancellationToken);

            await using (var createTable = new NpgsqlCommand(
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version VARCHAR(200) PRIMARY KEY,
                    applied_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
                )
                """, connection))
            {
                await createTable.ExecuteNonQueryAsync(cancellationToken);
            }

            var assembly = Assembly.GetExecutingAssembly();
            var migrations = assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith("Migrations.", StringComparison.Ordinal) &&
                    name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            if (migrations.Count == 0)
            {
                throw new InvalidOperationException("Tidak ada file migration yang tertanam pada assembly.");
            }

            foreach (var resourceName in migrations)
            {
                var version = resourceName["Migrations.".Length..];
                if (await IsAppliedAsync(connection, version, cancellationToken))
                {
                    continue;
                }

                var sql = await ReadResourceAsync(assembly, resourceName, cancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                try
                {
                    await using (var migrationCommand = new NpgsqlCommand(sql, connection, transaction))
                    {
                        await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await using (var recordCommand = new NpgsqlCommand(
                        "INSERT INTO schema_migrations (version) VALUES (@version)", connection, transaction))
                    {
                        recordCommand.Parameters.AddWithValue("version", version);
                        await recordCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await transaction.CommitAsync(cancellationToken);
                    logger.LogInformation("Migration {Version} diterapkan.", version);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
        }
        finally
        {
            await using var unlockCommand = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
            unlockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
            await unlockCommand.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task WaitForBaseSchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT to_regclass('public.transactions') IS NOT NULL", connection);
            var exists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            if (exists)
            {
                return;
            }

            logger.LogWarning("Schema dasar belum tersedia; menunggu inisialisasi database ({Attempt}/15).", attempt + 1);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new InvalidOperationException(
            "Tabel transactions tidak ditemukan. Jalankan database/schema.sql pada database baru.");
    }

    private static async Task<bool> IsAppliedAsync(
        NpgsqlConnection connection,
        string version,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE version = @version)", connection);
        command.Parameters.AddWithValue("version", version);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<string> ReadResourceAsync(
        Assembly assembly,
        string resourceName,
        CancellationToken cancellationToken)
    {
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Migration {resourceName} tidak dapat dibaca.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
