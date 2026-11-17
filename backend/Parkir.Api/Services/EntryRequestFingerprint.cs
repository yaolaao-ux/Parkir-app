using System.Security.Cryptography;
using System.Text;
using Parkir.Api.Models;

namespace Parkir.Api.Services;

public static class EntryRequestFingerprint
{
    public static string Create(EntryRequest request)
    {
        var canonical = string.Join('|',
            request.GateId.Trim().ToUpperInvariant(),
            request.VehicleTypeId,
            request.RfidUid?.Trim().ToUpperInvariant() ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
