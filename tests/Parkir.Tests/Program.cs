using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Parkir.Api.Models;
using Parkir.Api.Services;
using Parkir.Pos;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var failures = 0;
var tests = new (string Name, Action Run)[]
{
    ("durasi dibulatkan ke atas per menit", () =>
        AssertEqual(62, ParkingCalculator.GetDurationMinutes(
            new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 18, 9, 1, 1, TimeSpan.Zero)))),
    ("durasi tidak negatif", () =>
        AssertEqual(0, ParkingCalculator.GetDurationMinutes(
            new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero)))),
    ("grace period gratis", () =>
        AssertEqual(0m, ParkingCalculator.CalculateFee(Ticket(), 5))),
    ("satu jam memakai tarif pertama", () =>
        AssertEqual(5000m, ParkingCalculator.CalculateFee(Ticket(), 60))),
    ("dua jam menambah tarif per jam", () =>
        AssertEqual(8000m, ParkingCalculator.CalculateFee(Ticket(), 120))),
    ("24 jam dibatasi tarif harian", () =>
        AssertEqual(50000m, ParkingCalculator.CalculateFee(Ticket(), 1440))),
    ("25 jam memakai dua batas harian", () =>
        AssertEqual(77000m, ParkingCalculator.CalculateFee(Ticket(), 1500))),
    ("tarif harian nol berarti tanpa batas", () =>
        AssertEqual(74000m, ParkingCalculator.CalculateFee(Ticket(maxDailyRate: 0m), 1440))),
    ("member selalu gratis", () =>
        AssertEqual(0m, ParkingCalculator.CalculateFee(Ticket(isMember: true), 1440))),
    ("karcis hilang menjumlahkan denda dan tarif harian", () =>
        AssertEqual(100000m, ParkingCalculator.CalculateLostTicketTotal(Ticket()))),
    ("timezone bisnis menghasilkan batas hari WIB", () =>
    {
        var clock = CreateClock("Asia/Jakarta");
        AssertEqual("Asia/Jakarta", clock.TimeZoneId);
        AssertEqual(
            new DateTimeOffset(2026, 11, 17, 17, 0, 0, TimeSpan.Zero),
            clock.DayStartUtc(new DateOnly(2026, 11, 18)));
        AssertEqual(
            new DateTimeOffset(2026, 11, 18, 17, 0, 0, TimeSpan.Zero),
            clock.DayEndUtc(new DateOnly(2026, 11, 18)));
    }),
    ("timezone tidak valid ditolak", () =>
    {
        try
        {
            _ = CreateClock("Bukan/Zona");
            throw new Exception("Time zone tidak valid seharusnya ditolak.");
        }
        catch (InvalidOperationException)
        {
        }
    }),
    ("checkout lama tetap kompatibel", () =>
    {
        var request = JsonSerializer.Deserialize<CheckoutRequest>(
            """
            {"gateId":"GATE-OUT-01","paymentMethod":"CASH","lostTicket":false,
             "identityVerified":false,"photoOutPath":null,
             "idempotencyKey":"8f14e45f-ceea-467f-a1c4-7f7f7f7f7f7f"}
            """, jsonOptions)!;
        AssertEqual<long?>(null, request.VehicleTypeId);
        AssertEqual<string?>(null, request.LicensePlate);
        AssertEqual<string?>(null, request.OverrideReason);
        AssertEqual<decimal?>(null, request.ExpectedTotalFee);
    }),
    ("checkout baru membaca kelas, plat, dan alasan", () =>
    {
        var request = JsonSerializer.Deserialize<CheckoutRequest>(
            """
            {"gateId":"GATE-OUT-01","paymentMethod":"QRIS","lostTicket":false,
             "identityVerified":false,"photoOutPath":null,
             "idempotencyKey":"8f14e45f-ceea-467f-a1c4-7f7f7f7f7f7f",
             "vehicleTypeId":2,"licensePlate":"B 1234 XYZ",
             "overrideReason":"Koreksi kelas dari petugas","expectedTotalFee":8000}
            """, jsonOptions)!;
        AssertEqual<long?>(2, request.VehicleTypeId);
        AssertEqual("B 1234 XYZ", request.LicensePlate);
        AssertEqual("Koreksi kelas dari petugas", request.OverrideReason);
        AssertEqual<decimal?>(8000m, request.ExpectedTotalFee);
    }),
    ("entry lama tetap kompatibel", () =>
    {
        var request = JsonSerializer.Deserialize<EntryRequest>(
            """{"gateId":"GATE-IN-01","vehicleTypeId":1,"photoInPath":null,"rfidUid":null}""",
            jsonOptions)!;
        AssertEqual<string?>(null, request.IdempotencyKey);
    }),
    ("entry idempotency terbaca", () =>
    {
        var request = JsonSerializer.Deserialize<EntryRequest>(
            """
            {"gateId":"GATE-IN-01","vehicleTypeId":1,"photoInPath":null,"rfidUid":null,
             "idempotencyKey":"GATE-IN-01-12345"}
            """, jsonOptions)!;
        AssertEqual("GATE-IN-01-12345", request.IdempotencyKey);
    }),
    ("ticket id memakai 128-bit acak dan tanggal bisnis", () =>
    {
        var businessTime = new DateTimeOffset(2026, 11, 18, 1, 2, 3, TimeSpan.FromHours(7));
        var ids = Enumerable.Range(0, 1000)
            .Select(_ => TicketIdGenerator.Create(businessTime))
            .ToList();
        AssertEqual(45, ids[0].Length);
        AssertTrue(ids[0].StartsWith("TKT-20261118-", StringComparison.Ordinal));
        AssertEqual(1000, ids.Distinct().Count());
    }),
    ("entry fingerprint stabil tetapi berbeda untuk payload berbeda", () =>
    {
        var request = new EntryRequest("GATE-IN-01", 1, "2026-11-18/a.jpg", "ABC123");
        var same = new EntryRequest("gate-in-01", 1, "2026-11-18/b.jpg", "abc123");
        var differentGate = new EntryRequest("GATE-IN-02", 1, "2026-11-18/a.jpg", "ABC123");
        var differentType = new EntryRequest("GATE-IN-01", 2, "2026-11-18/a.jpg", "ABC123");
        var differentRfid = new EntryRequest("GATE-IN-01", 1, "2026-11-18/a.jpg", "XYZ999");
        AssertEqual(EntryRequestFingerprint.Create(request), EntryRequestFingerprint.Create(same));
        AssertTrue(EntryRequestFingerprint.Create(request) != EntryRequestFingerprint.Create(differentGate));
        AssertTrue(EntryRequestFingerprint.Create(request) != EntryRequestFingerprint.Create(differentType));
        AssertTrue(EntryRequestFingerprint.Create(request) != EntryRequestFingerprint.Create(differentRfid));
        AssertEqual(64, EntryRequestFingerprint.Create(request).Length);
    }),
    ("antrean checkout bertransisi PENDING ke CONFIRMED ke HARDWARE_DONE", () =>
        WithQueue(queue =>
        {
            var item = Pending("TKT-QUEUE-1");
            queue.Enqueue(item);
            var now = DateTimeOffset.UtcNow;
            AssertEqual(1, queue.GetWorkItems(now).Count);
            AssertEqual(1, queue.GetOutstandingCount());

            queue.MarkRetry(item.IdempotencyKey, "timeout", TimeSpan.FromMinutes(1));
            AssertEqual(0, queue.GetWorkItems(now).Count);
            AssertEqual(1, queue.GetWorkItems(now.AddMinutes(2))[0].AttemptCount);

            queue.MarkConfirmed(item.IdempotencyKey, "{\"ticketId\":\"TKT-QUEUE-1\"}");
            var confirmed = queue.GetWorkItems(now.AddMinutes(2));
            AssertEqual(1, confirmed.Count);
            AssertEqual("CONFIRMED", confirmed[0].Status);

            queue.MarkHardwareDone(item.IdempotencyKey);
            AssertEqual(0, queue.GetWorkItems(now.AddMinutes(2)).Count);
            AssertEqual(0, queue.GetOutstandingCount());
        })),
    ("antrean 4xx permanen masuk FAILED dan bisa di-retry", () =>
        WithQueue(queue =>
        {
            var item = Pending("TKT-QUEUE-2");
            queue.Enqueue(item);
            queue.MarkFailed(item.IdempotencyKey, "HTTP 409: tiket sudah dibayar");
            AssertEqual(1, queue.GetFailedCount());
            AssertEqual(0, queue.GetWorkItems(DateTimeOffset.UtcNow).Count);

            queue.RetryFailed();
            AssertEqual(0, queue.GetFailedCount());
            AssertEqual(1, queue.GetWorkItems(DateTimeOffset.UtcNow).Count);
        })),
    ("database antrean lama dimigrasikan tanpa kehilangan data", () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "parkir-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var dataSource = Path.Combine(directory, "checkout-outbox.db");
            using (var connection = new SqliteConnection($"Data Source={dataSource}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE pending_checkouts (
                        idempotency_key TEXT PRIMARY KEY,
                        ticket_id TEXT NOT NULL,
                        payload TEXT NOT NULL,
                        created_at TEXT NOT NULL
                    );
                    INSERT INTO pending_checkouts VALUES
                        ('8f14e45f-ceea-467f-a1c4-7f7f7f7f7f7f', 'TKT-OLD', '{}', $created);
                    """;
                command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
                command.ExecuteNonQuery();
            }

            var queue = new CheckoutQueue(directory);
            var items = queue.GetWorkItems(DateTimeOffset.UtcNow);
            AssertEqual(1, items.Count);
            AssertEqual("TKT-OLD", items[0].TicketId);
            AssertEqual("PENDING", items[0].Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }),
    ("migration tertanam lengkap untuk runner startup", () =>
    {
        var resources = typeof(ParkingService).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("Migrations.", StringComparison.Ordinal) &&
                name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        AssertEqual(9, resources.Count);
        AssertEqual("Migrations.002_add_checkout_idempotency.sql", resources[0]);
        AssertEqual("Migrations.010_add_anti_passback_index.sql", resources[^1]);
    }),
    ("endpoint perangkat menolak request tanpa API key termasuk trailing slash", () =>
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[]
        {
            "/api/entries",
            "/api/entries/",
            "/api/devices/heartbeat",
            "/api/devices/heartbeat/"
        })
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = client.PostAsync(path, content).GetAwaiter().GetResult();
            AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        foreach (var path in new[]
        {
            "/api/media/entries",
            "/api/media/entries/"
        })
        {
            using var content = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF });
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            using var response = client.PostAsync(path, content).GetAwaiter().GetResult();
            AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var memberResponse = client.GetAsync("/api/members/TEST/verify").GetAwaiter().GetResult();
        AssertEqual(HttpStatusCode.Unauthorized, memberResponse.StatusCode);
    })
};

foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL  {name}");
        Console.WriteLine($"      {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"{tests.Length - failures}/{tests.Length} test lulus.");
return failures == 0 ? 0 : 1;

static PendingCheckout Pending(string ticketId) => new(
    Guid.NewGuid(),
    ticketId,
    "{}",
    DateTimeOffset.UtcNow,
    "PENDING",
    0,
    null,
    null,
    false);

static void WithQueue(Action<CheckoutQueue> action)
{
    var directory = Path.Combine(Path.GetTempPath(), "parkir-queue-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        action(new CheckoutQueue(directory));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static WebApplicationFactory<Parkir.Api.Services.ParkingService> CreateFactory() =>
    new WebApplicationFactory<Parkir.Api.Services.ParkingService>().WithWebHostBuilder(builder =>
    {
        builder.UseContentRoot(FindApiRoot());
        builder.UseSetting("ConnectionStrings:Parking",
            "Host=127.0.0.1;Port=5999;Database=parkir;Username=test;Password=test");
        builder.UseSetting("Security:ApiKey", "test-api-key");
        builder.UseSetting("Security:JwtSigningKey", "01234567890123456789012345678901");
        builder.UseSetting("Database:AutoMigrate", "false");
    });

static string FindApiRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        var candidate = Path.Combine(directory.FullName, "backend", "Parkir.Api", "Parkir.Api.csproj");
        if (File.Exists(candidate))
        {
            return Path.GetDirectoryName(candidate)!;
        }
    }

    throw new DirectoryNotFoundException("Folder backend/Parkir.Api tidak ditemukan dari test output.");
}

static BusinessClock CreateClock(string timeZoneId) =>    new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Business:TimeZone"] = timeZoneId
        })
        .Build());

static TicketData Ticket(bool isMember = false, decimal maxDailyRate = 50000m) =>
    new(
        "TKT-TEST",
        new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero),
        null,
        "UNPAID",
        isMember,
        null,
        5,
        5000m,
        3000m,
        50000m,
        maxDailyRate,
        1,
        "Mobil");

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"Expected: {expected}; Actual: {actual}");
    }
}

static void AssertTrue(bool condition, string? message = null)
{
    if (!condition)
    {
        throw new Exception(message ?? "Expected condition to be true.");
    }
}
