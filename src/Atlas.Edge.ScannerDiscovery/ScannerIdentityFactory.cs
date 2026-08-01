using System.Security.Cryptography;
using System.Text;

namespace Atlas.Edge.ScannerDiscovery;

public sealed class ScannerIdentityFactory : IScannerIdentityFactory
{
    public ScannerStableIdentity Create(AdapterScannerDevice scanner)
    {
        ArgumentNullException.ThrowIfNull(scanner);

        var sourceId = Normalize(scanner.SourceId);
        if (sourceId is not null && scanner.HasProviderStableIdentity)
        {
            return new ScannerStableIdentity(
                CreateId($"provider|{scanner.Protocol}|{sourceId}"),
                CreateHash($"{scanner.Protocol}|{sourceId}", 16),
                HashDevicePath(scanner.DevicePath),
                ScannerMetadataConfidence.ProviderStableIdentity);
        }

        var serial = Normalize(scanner.SerialNumber);
        if (serial is not null)
        {
            return new ScannerStableIdentity(
                CreateId($"serial|{Normalize(scanner.Manufacturer)}|{Normalize(scanner.Model)}|{serial}"),
                CreateHash($"{scanner.Protocol}|serial|{serial}", 16),
                null,
                ScannerMetadataConfidence.SerialIdentity);
        }

        var devicePath = Normalize(scanner.DevicePath);
        if (devicePath is not null)
        {
            return new ScannerStableIdentity(
                CreateId($"path|{devicePath}"),
                CreateHash($"{scanner.Protocol}|path|{devicePath}", 16),
                CreateHash(devicePath, 24),
                ScannerMetadataConfidence.DevicePathIdentity);
        }

        var fallback = string.Join('|',
            scanner.Protocol,
            Normalize(scanner.Manufacturer) ?? "unknown",
            Normalize(scanner.Model) ?? "unknown",
            Normalize(scanner.Driver.Name) ?? "unknown",
            Normalize(scanner.Driver.Version) ?? "unknown",
            Normalize(scanner.Interface) ?? "unknown",
            sourceId ?? "unknown");

        return new ScannerStableIdentity(
            CreateId($"metadata|{fallback}"),
            CreateHash(fallback, 16),
            null,
            ScannerMetadataConfidence.MetadataFallback);
    }

    internal static string CreateId(string value) => $"scanner-{CreateHash(value, 24)}";

    internal static string CreateHash(string value, int hexadecimalCharacters)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant()[..hexadecimalCharacters];
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? HashDevicePath(string? value)
    {
        var normalized = Normalize(value);
        return normalized is null ? null : CreateHash(normalized, 24);
    }
}
