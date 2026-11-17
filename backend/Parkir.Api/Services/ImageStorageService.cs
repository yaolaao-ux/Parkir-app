using System.Globalization;

namespace Parkir.Api.Services;

public sealed record StoredImage(string Path, string ContentType, string FullPath);

public sealed class ImageStorageService
{
    private const int MaxImageBytes = 5 * 1024 * 1024;
    private readonly string _rootDirectory;
    private readonly BusinessClock _clock;

    public ImageStorageService(IWebHostEnvironment environment, string? configuredRoot, BusinessClock clock)
    {
        _rootDirectory = Path.GetFullPath(configuredRoot ?? Path.Combine(environment.ContentRootPath, "storage", "images"));
        _clock = clock;
    }

    public async Task<StoredImage> StoreAsync(Stream source, long? contentLength, string? contentType, CancellationToken cancellationToken)
    {
        if (contentLength is null or <= 0 or > MaxImageBytes)
        {
            throw new ArgumentException("Ukuran gambar harus antara 1 byte dan 5 MB.");
        }

        var bytes = new byte[(int)contentLength.Value];
        var read = 0;
        while (read < bytes.Length)
        {
            var currentRead = await source.ReadAsync(bytes.AsMemory(read), cancellationToken);
            if (currentRead == 0)
            {
                throw new ArgumentException("Data gambar tidak lengkap.");
            }
            read += currentRead;
        }

        var detectedType = DetectContentType(bytes);
        if (!string.Equals(contentType, detectedType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Content-Type tidak sesuai dengan format gambar.");
        }

        var date = _clock.FileDate;
        var extension = detectedType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new ArgumentException("Format gambar tidak didukung.")
        };
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var directory = Path.Combine(_rootDirectory, date);
        Directory.CreateDirectory(directory);
        var fullPath = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
        return new StoredImage($"{date}/{fileName}", detectedType, fullPath);
    }

    public StoredImage? Find(string date, string fileName)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            !Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out _))
        {
            return null;
        }

        var contentType = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null
        };
        if (contentType is null)
        {
            return null;
        }

        var fullPath = Path.Combine(_rootDirectory, date, fileName);
        return File.Exists(fullPath) ? new StoredImage($"{date}/{fileName}", contentType, fullPath) : null;
    }

    private static string DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        throw new ArgumentException("Isi file bukan JPEG, PNG, atau WebP.");
    }
}
