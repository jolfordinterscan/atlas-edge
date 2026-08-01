using System.Security.Cryptography;
using System.Text;

namespace Atlas.Edge.Security;

public static class SecretRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(digest)[..12].ToLowerInvariant()}";
    }
}
