using System.Security.Cryptography;
using System.Text;

namespace Atlas.Edge.ScannerEvidence;

internal static class EvidenceIdentity
{
    public static string Hash(string purpose, params string?[] values)
    {
        var normalized = string.Join('|', values.Select(value =>
            string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToUpperInvariant()));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}|{normalized}"));
        return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }
}
