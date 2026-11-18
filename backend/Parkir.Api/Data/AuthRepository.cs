using Npgsql;
using Parkir.Api.Models;

namespace Parkir.Api.Data;

public sealed class AuthRepository(NpgsqlDataSource dataSource)
{
    public async Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, username, full_name, role, password_hash
            FROM users
            WHERE username = @username AND is_active = TRUE
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("username", username);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new UserAccount(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4))
            : null;
    }

    public async Task<bool?> IsActiveAsync(long userId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT is_active FROM users WHERE id = @id";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", userId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? null : (bool)result;
    }
}