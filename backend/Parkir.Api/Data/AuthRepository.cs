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
            WHERE username = @username
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("username", username);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new UserAccount(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4))
            : null;
    }
}