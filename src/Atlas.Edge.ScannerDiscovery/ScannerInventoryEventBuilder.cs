using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atlas.Edge.Core;

namespace Atlas.Edge.ScannerDiscovery;

public interface IScannerInventoryEventBuilder
{
    ScannerInventoryEvent Build(ScannerDiscoverySnapshot snapshot, AgentIdentity identity);

    string Fingerprint(ScannerDiscoverySnapshot snapshot);
}

public sealed class ScannerInventoryEventBuilder : IScannerInventoryEventBuilder
{
    public const string EventType = "scanner.inventory";
    public const string SchemaVersion = "1.0";

    public ScannerInventoryEvent Build(ScannerDiscoverySnapshot snapshot, AgentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(identity);

        var entries = ToEntries(snapshot);
        return new ScannerInventoryEvent(
            Guid.NewGuid().ToString("N"),
            EventType,
            SchemaVersion,
            snapshot.DiscoveredAtUtc.ToUniversalTime(),
            identity.AgentId,
            identity.WorkstationId,
            Fingerprint(entries),
            entries.Count,
            entries);
    }

    public string Fingerprint(ScannerDiscoverySnapshot snapshot) => Fingerprint(ToEntries(snapshot));

    private static IReadOnlyList<ScannerInventoryEntry> ToEntries(ScannerDiscoverySnapshot snapshot) =>
        snapshot.Scanners
            .OrderBy(scanner => scanner.DiscoveryId, StringComparer.Ordinal)
            .Select(scanner => new ScannerInventoryEntry(
                scanner.DiscoveryId,
                scanner.ProviderId,
                scanner.ProviderName,
                scanner.Manufacturer,
                scanner.Model,
                scanner.SerialNumber,
                scanner.DevicePathHash,
                scanner.Drivers.FirstOrDefault()?.Name,
                scanner.Drivers.FirstOrDefault()?.Version,
                string.Join(',', scanner.Protocols.OrderBy(value => value)),
                scanner.ConnectionType.ToString(),
                scanner.FirmwareVersion,
                scanner.Status.ToString(),
                scanner.Status is not ScannerOperationalStatus.Offline and
                    not ScannerOperationalStatus.Unavailable,
                scanner.NormalizedCapabilities.Select(value => value.ToString()).OrderBy(value => value).ToArray(),
                scanner.FirstObservedUtc.ToUniversalTime(),
                scanner.LastObservedUtc.ToUniversalTime(),
                scanner.MetadataConfidence.ToString(),
                scanner.DiscoveryWarnings.OrderBy(value => value, StringComparer.Ordinal).ToArray()))
            .ToArray();

    private static string Fingerprint(IReadOnlyList<ScannerInventoryEntry> entries)
    {
        var stableEntries = entries.Select(entry => entry with
        {
            FirstObservedUtc = default,
            LastObservedUtc = default
        });
        var json = JsonSerializer.Serialize(stableEntries);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}
