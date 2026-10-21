using System.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Parking");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set ConnectionStrings__Parking terlebih dahulu.");
    return 1;
}

var createExampleAdmin = args.Contains("--example-admin", StringComparer.Ordinal);
var username = createExampleAdmin ? "admin-demo" : ReadValue("Username: ");
var fullName = createExampleAdmin ? "Administrator Demo" : ReadValue("Nama lengkap: ");
var role = createExampleAdmin ? "ADMIN" : ReadValue("Role (ADMIN/CASHIER): ").ToUpperInvariant();
if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName) || role is not ("ADMIN" or "CASHIER"))
{
    Console.Error.WriteLine("Username, nama, dan role ADMIN/CASHIER yang valid wajib diisi.");
    return 1;
}

var password = createExampleAdmin
    ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    : ReadPassword();
if (password.Length < 12)
{
    Console.Error.WriteLine("Password harus memiliki minimal 12 karakter.");
    return 1;
}

var account = new ProvisionedAccount(username);
var passwordHash = new PasswordHasher<ProvisionedAccount>().HashPassword(account, password);
await using var dataSource = NpgsqlDataSource.Create(connectionString);
await using var connection = await dataSource.OpenConnectionAsync();
await using var command = new NpgsqlCommand("""
    INSERT INTO users (username, password_hash, full_name, role)
    VALUES (@username, @password_hash, @full_name, @role)
    """, connection);
command.Parameters.AddWithValue("username", username);
command.Parameters.AddWithValue("password_hash", passwordHash);
command.Parameters.AddWithValue("full_name", fullName);
command.Parameters.AddWithValue("role", role);
await command.ExecuteNonQueryAsync();
if (createExampleAdmin)
{
    Console.WriteLine("Akun administrator contoh berhasil dibuat. Simpan password ini sekarang:");
    Console.WriteLine($"Username: {username}");
    Console.WriteLine($"Password: {password}");
}
else
{
    Console.WriteLine($"Akun {username} ({role}) berhasil dibuat.");
}
return 0;

static string ReadValue(string prompt)
{
    Console.Write(prompt);
    return Console.ReadLine()?.Trim() ?? string.Empty;
}

static string ReadPassword()
{
    Console.Write("Password: ");
    return ReadHiddenPassword();
}

static string ReadHiddenPassword()
{
    var password = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return password.ToString();
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
            {
                password.Length--;
            }
            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            password.Append(key.KeyChar);
        }
    }
}

internal sealed record ProvisionedAccount(string Username);