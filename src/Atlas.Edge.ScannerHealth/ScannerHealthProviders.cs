using System.Collections.Immutable;
using System.Globalization;
using Atlas.Edge.ScannerDiscovery;

namespace Atlas.Edge.ScannerHealth;

public sealed class WiaScannerHealthProvider : IScannerHealthProvider
{
    private readonly IWiaScannerSourceCatalog _catalog;

    public WiaScannerHealthProvider(IWiaScannerSourceCatalog catalog) => _catalog = catalog;

    public ScannerProtocol Protocol => ScannerProtocol.Wia;

    public async Task<ScannerHealthProviderResult> CollectAsync(CancellationToken cancellationToken) =>
        Map(await _catalog.EnumerateAsync(cancellationToken), Protocol, "wia_runtime_unavailable");

    internal static ScannerHealthProviderResult Map(
        ScannerSourceCatalogResult result,
        ScannerProtocol protocol,
        string unavailableCode) =>
        result.IsAvailable
            ? ScannerHealthProviderResult.Available(
                protocol,
                result.Sources.Select(source => ScannerHealthMetadataParser.Parse(source, protocol)))
            : ScannerHealthProviderResult.Unavailable(protocol, unavailableCode);
}

public sealed class TwainScannerHealthProvider : IScannerHealthProvider
{
    private readonly ITwainScannerSourceCatalog _catalog;

    public TwainScannerHealthProvider(ITwainScannerSourceCatalog catalog) => _catalog = catalog;

    public ScannerProtocol Protocol => ScannerProtocol.Twain;

    public async Task<ScannerHealthProviderResult> CollectAsync(CancellationToken cancellationToken) =>
        WiaScannerHealthProvider.Map(
            await _catalog.EnumerateAsync(cancellationToken),
            Protocol,
            "twain_runtime_unavailable");
}

public sealed class IsisScannerHealthProvider : IScannerHealthProvider
{
    private readonly IIsisScannerSourceCatalog _catalog;

    public IsisScannerHealthProvider(IIsisScannerSourceCatalog catalog) => _catalog = catalog;

    public ScannerProtocol Protocol => ScannerProtocol.Isis;

    public async Task<ScannerHealthProviderResult> CollectAsync(CancellationToken cancellationToken) =>
        WiaScannerHealthProvider.Map(
            await _catalog.EnumerateAsync(cancellationToken),
            Protocol,
            "isis_runtime_unavailable");
}

public sealed class MockScannerHealthProvider : IScannerHealthProvider
{
    private readonly ImmutableArray<ScannerHealthReading> _readings;

    public MockScannerHealthProvider()
        : this([CreateDefaultReading()])
    {
    }

    public MockScannerHealthProvider(IReadOnlyList<ScannerHealthReading> readings) =>
        _readings = readings.ToImmutableArray();

    public ScannerProtocol Protocol => ScannerProtocol.Mock;

    public Task<ScannerHealthProviderResult> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ScannerHealthProviderResult.Available(Protocol, _readings));
    }

    private static ScannerHealthReading CreateDefaultReading() =>
        new(
            "mock-scanner-001",
            ScannerProtocol.Mock,
            "Atlas Mock Devices",
            "Document Scanner",
            "MOCK-SERIAL-001",
            125000,
            450,
            72m,
            64m,
            [new ScannerConsumableHealth("separation-roller", 72m, "ready")],
            true,
            ImmutableDictionary<string, long>.Empty.Add("cleaning-cycles", 12),
            true,
            "0.0-mock",
            58m,
            60m,
            1,
            0,
            0,
            ScannerOnlineStatus.Online,
            ScannerDriverHealthStatus.Ready,
            new ScannerUsbStability(0, null),
            TimeSpan.FromHours(72));
}

internal static class ScannerHealthMetadataParser
{
    public static ScannerHealthReading Parse(ScannerSourceMetadata source, ScannerProtocol protocol)
    {
        var metadata = source.Metadata ?? new Dictionary<string, string>();
        var consumables = ParseConsumables(metadata);
        var maintenanceCounters = ParseMaintenanceCounters(metadata);

        return new ScannerHealthReading(
            source.SourceId,
            protocol,
            source.Manufacturer,
            source.Model,
            source.SerialNumber,
            ReadLong(metadata, "Lifetime Pages", "Total Pages", "Page Count"),
            ReadLong(metadata, "Daily Pages", "Pages Today"),
            ReadPercent(metadata, "Roller Life", "Roller Remaining"),
            ReadPercent(metadata, "Pad Life", "Pad Remaining"),
            consumables,
            HasPrefix(metadata, "Consumable.", "Consumable:"),
            maintenanceCounters,
            HasPrefix(metadata, "Maintenance.", "Maintenance:"),
            source.FirmwareVersion ?? ReadString(metadata, "Firmware Version", "Firmware"),
            ReadDecimal(metadata, "Scan Speed", "Pages Per Minute", "Current PPM"),
            ReadDecimal(metadata, "Rated Scan Speed", "Rated PPM"),
            ReadLong(metadata, "Jam Count", "Paper Jam Count"),
            ReadLong(metadata, "Double Feed Count", "Double-Feed Count"),
            ReadLong(metadata, "Transport Error Count", "Transport Errors"),
            source.OnlineStatus,
            ParseDriverStatus(ReadString(metadata, "Driver Status")),
            ParseUsbStability(metadata),
            ParseUptime(metadata));
    }

    private static ImmutableArray<ScannerConsumableHealth> ParseConsumables(
        IReadOnlyDictionary<string, string> metadata) =>
        metadata
            .Where(item => HasAnyPrefix(item.Key, "Consumable.", "Consumable:"))
            .Select(item =>
            {
                var name = item.Key[(item.Key.IndexOfAny(['.', ':']) + 1)..].Trim();
                var parts = item.Value.Split('|', StringSplitOptions.TrimEntries);
                return new ScannerConsumableHealth(
                    name,
                    TryPercent(parts[0]),
                    parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : null);
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToImmutableArray();

    private static ImmutableDictionary<string, long> ParseMaintenanceCounters(
        IReadOnlyDictionary<string, string> metadata) =>
        metadata
            .Where(item => HasAnyPrefix(item.Key, "Maintenance.", "Maintenance:"))
            .Select(item => new
            {
                Name = item.Key[(item.Key.IndexOfAny(['.', ':']) + 1)..].Trim(),
                Value = TryLong(item.Value)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && item.Value.HasValue)
            .ToImmutableDictionary(item => item.Name, item => item.Value!.Value, StringComparer.OrdinalIgnoreCase);

    private static ScannerUsbStability? ParseUsbStability(IReadOnlyDictionary<string, string> metadata)
    {
        var disconnectCount = ReadLong(metadata, "USB Disconnect Count", "USB Disconnects");
        var lastDisconnect = ReadString(metadata, "USB Last Disconnect", "Last USB Disconnect");
        var parsedLastDisconnect = DateTimeOffset.TryParse(
            lastDisconnect,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp)
                ? timestamp
                : (DateTimeOffset?)null;

        return disconnectCount.HasValue || parsedLastDisconnect.HasValue
            ? new ScannerUsbStability(disconnectCount, parsedLastDisconnect)
            : null;
    }

    private static TimeSpan? ParseUptime(IReadOnlyDictionary<string, string> metadata)
    {
        var seconds = ReadLong(metadata, "Device Uptime Seconds", "Uptime Seconds");
        return seconds.HasValue && seconds.Value <= TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(seconds.Value)
            : null;
    }

    private static ScannerDriverHealthStatus ParseDriverStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ScannerDriverHealthStatus.Unknown;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "READY" or "OK" or "HEALTHY" => ScannerDriverHealthStatus.Ready,
            "DEGRADED" or "WARNING" => ScannerDriverHealthStatus.Degraded,
            "ERROR" or "FAILED" or "UNAVAILABLE" => ScannerDriverHealthStatus.Error,
            _ => ScannerDriverHealthStatus.Unknown
        };
    }

    private static long? ReadLong(IReadOnlyDictionary<string, string> metadata, params string[] names) =>
        TryLong(ReadString(metadata, names));

    private static decimal? ReadPercent(IReadOnlyDictionary<string, string> metadata, params string[] names) =>
        TryPercent(ReadString(metadata, names));

    private static decimal? ReadDecimal(IReadOnlyDictionary<string, string> metadata, params string[] names)
    {
        var value = ReadString(metadata, names);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;
    }

    private static string? ReadString(IReadOnlyDictionary<string, string> metadata, params string[] names)
    {
        foreach (var name in names)
        {
            var match = metadata.FirstOrDefault(item => string.Equals(
                item.Key.Trim(),
                name,
                StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                return match.Value.Trim();
            }
        }

        return null;
    }

    private static long? TryLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;

    private static decimal? TryPercent(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 0 and <= 100
            ? parsed
            : null;

    private static bool HasPrefix(IReadOnlyDictionary<string, string> metadata, params string[] prefixes) =>
        metadata.Keys.Any(key => HasAnyPrefix(key, prefixes));

    private static bool HasAnyPrefix(string value, params string[] prefixes) =>
        prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
