using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Atlas.Edge.ScannerDiscovery;

public sealed record WindowsScannerMetadataRecord(
    string DeviceInstanceId,
    IReadOnlyList<string> HardwareIds,
    string? ContainerId,
    IReadOnlyList<string> LocationPaths,
    string? Manufacturer,
    string? FriendlyName,
    string? DriverName,
    string? DriverProvider,
    string? DriverVersion)
{
    public string? FirmwareVersion { get; init; }
}

public static class ScannerMetadataPrivacy
{
    public static string MaskSerial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Unknown";
        var trimmed = value.Trim();
        var suffixLength = Math.Min(4, trimmed.Length);
        return $"****{trimmed[^suffixLength..]}";
    }
}

public interface IPnpScannerMetadataCatalog
{
    Task<(bool IsAvailable, IReadOnlyList<WindowsScannerMetadataRecord> Records)> ReadAsync(
        CancellationToken cancellationToken);
}

public interface IRegistryScannerMetadataCatalog
{
    Task<(bool IsAvailable, IReadOnlyList<WindowsScannerMetadataRecord> Records)> ReadAsync(
        CancellationToken cancellationToken);
}

public sealed class ScannerMetadataEnricher : IScannerMetadataEnricher
{
    private readonly IReadOnlyList<IScannerMetadataProvider> _providers;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _providerTimeout;

    public ScannerMetadataEnricher(
        IEnumerable<IScannerMetadataProvider> providers,
        TimeProvider timeProvider,
        TimeSpan providerTimeout)
    {
        _providers = providers.ToArray();
        _timeProvider = timeProvider;
        _providerTimeout = providerTimeout > TimeSpan.Zero
            ? providerTimeout
            : throw new ArgumentOutOfRangeException(nameof(providerTimeout));
    }

    public async Task<IReadOnlyList<AdapterScannerDevice>> EnrichAsync(
        IReadOnlyList<AdapterScannerDevice> scanners,
        CancellationToken cancellationToken)
    {
        var enriched = new List<AdapterScannerDevice>(scanners.Count);
        foreach (var scanner in scanners)
        {
            var metadata = scanner.EnrichedMetadata;
            foreach (var provider in _providers)
            {
                try
                {
                    var result = await provider.GetMetadataAsync(scanner, cancellationToken)
                        .WaitAsync(_providerTimeout, _timeProvider, cancellationToken);
                    if (result.Metadata is not null)
                    {
                        metadata = Merge(metadata, result.Metadata);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (TimeoutException)
                {
                    // Absence remains unknown; other providers and scanners continue.
                }
                catch (Exception)
                {
                    // Raw platform exceptions never leave the enrichment boundary.
                }
            }

            enriched.Add(scanner with
            {
                SerialNumber = First(scanner.SerialNumber, metadata?.SerialNumber),
                FirmwareVersion = First(scanner.FirmwareVersion, metadata?.FirmwareVersion),
                Driver = scanner.Driver with
                {
                    Name = First(metadata?.DriverName, scanner.Driver.Name) ?? "Unknown",
                    Version = First(metadata?.DriverVersion, scanner.Driver.Version),
                    Provider = First(metadata?.DriverProvider, scanner.Driver.Provider)
                },
                EnrichedMetadata = metadata
            });
        }

        return enriched;
    }

    private static ScannerMetadata Merge(ScannerMetadata? current, ScannerMetadata incoming) =>
        new(
            First(current?.SerialNumber, incoming.SerialNumber),
            First(current?.SerialSource, incoming.SerialSource),
            First(current?.HardwareId, incoming.HardwareId),
            First(current?.DriverName, incoming.DriverName),
            First(current?.DriverProvider, incoming.DriverProvider),
            First(current?.DriverVersion, incoming.DriverVersion),
            First(current?.UsbVendorId, incoming.UsbVendorId),
            First(current?.UsbProductId, incoming.UsbProductId),
            First(current?.ContainerId, incoming.ContainerId),
            First(current?.LocationPathHash, incoming.LocationPathHash),
            First(current?.FriendlyName, incoming.FriendlyName),
            First(current?.DeviceInstanceIdHash, incoming.DeviceInstanceIdHash))
        {
            FirmwareVersion = First(current?.FirmwareVersion, incoming.FirmwareVersion)
        };

    private static string? First(string? current, string? incoming) =>
        !string.IsNullOrWhiteSpace(current) && !string.Equals(current, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? current
            : string.IsNullOrWhiteSpace(incoming) ? current : incoming;
}

public sealed class WindowsPnpScannerMetadataProvider : IPnpScannerMetadataProvider
{
    private readonly IPnpScannerMetadataCatalog _catalog;

    public WindowsPnpScannerMetadataProvider(IPnpScannerMetadataCatalog catalog) => _catalog = catalog;

    public string ProviderName => "WindowsPnP";

    public async Task<ScannerMetadataProviderResult> GetMetadataAsync(
        AdapterScannerDevice scanner,
        CancellationToken cancellationToken)
    {
        var result = await _catalog.ReadAsync(cancellationToken);
        if (!result.IsAvailable)
        {
            return ScannerMetadataProviderResult.Unavailable("pnp_unavailable");
        }

        var matches = result.Records
            .Select(record => (Record: record, Score: Score(scanner, record)))
            .Where(value => value.Score >= 50)
            .OrderByDescending(value => value.Score)
            .ToArray();
        if (matches.Length == 0 || (matches.Length > 1 && matches[0].Score == matches[1].Score))
        {
            return ScannerMetadataProviderResult.Available(null);
        }

        return ScannerMetadataProviderResult.Available(Normalize(matches[0].Record));
    }

    private static int Score(AdapterScannerDevice scanner, WindowsScannerMetadataRecord record)
    {
        var identity = $"{scanner.SourceId}|{scanner.DevicePath}";
        var score = identity.Contains(record.DeviceInstanceId, StringComparison.OrdinalIgnoreCase) ? 100 : 0;
        var idText = string.Join('|', record.HardwareIds);
        foreach (var token in ExtractUsbIds(idText))
        {
            if (identity.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 60;
            }
        }

        if (ContainsEither(record.FriendlyName, scanner.Model)) score += 20;
        if (ContainsEither(record.Manufacturer, scanner.Manufacturer)) score += 10;
        return score;
    }

    public static ScannerMetadata Normalize(WindowsScannerMetadataRecord record)
    {
        var allIds = string.Join('|', record.HardwareIds);
        var usb = ExtractUsbIds(allIds).ToArray();
        var serial = ExtractUsbSerial(record.DeviceInstanceId);
        return new ScannerMetadata(
            serial,
            serial is null ? null : "WindowsPnPDeviceInstance",
            Hash(record.HardwareIds.FirstOrDefault()),
            Bound(record.DriverName),
            Bound(record.DriverProvider),
            Bound(record.DriverVersion),
            usb.FirstOrDefault(value => value.StartsWith("VID_", StringComparison.OrdinalIgnoreCase))?[4..].ToUpperInvariant(),
            usb.FirstOrDefault(value => value.StartsWith("PID_", StringComparison.OrdinalIgnoreCase))?[4..].ToUpperInvariant(),
            Hash(record.ContainerId),
            Hash(record.LocationPaths.FirstOrDefault()),
            Bound(record.FriendlyName),
            Hash(record.DeviceInstanceId))
        {
            FirmwareVersion = Bound(record.FirmwareVersion)
        };
    }

    public static string? Hash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IEnumerable<string> ExtractUsbIds(string value)
    {
        foreach (var part in value.Split(new[] { '\\', '&', '#' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("VID_", StringComparison.OrdinalIgnoreCase) ||
                part.StartsWith("PID_", StringComparison.OrdinalIgnoreCase))
            {
                yield return part.Length >= 8 ? part[..8] : part;
            }
        }
    }

    private static string? ExtractUsbSerial(string deviceInstanceId)
    {
        if (!deviceInstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)) return null;
        var candidate = deviceInstanceId.Split('\\').LastOrDefault();
        return string.IsNullOrWhiteSpace(candidate) || candidate.Contains('&') ? null : Bound(candidate);
    }

    private static bool ContainsEither(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        (left.Contains(right, StringComparison.OrdinalIgnoreCase) || right.Contains(left, StringComparison.OrdinalIgnoreCase));

    private static string? Bound(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 256)];
}

public sealed class WindowsRegistryScannerMetadataProvider : IRegistryScannerMetadataProvider
{
    private readonly WindowsPnpScannerMetadataProvider _inner;

    public WindowsRegistryScannerMetadataProvider(IRegistryScannerMetadataCatalog catalog) =>
        _inner = new WindowsPnpScannerMetadataProvider(new RegistryCatalogAdapter(catalog));

    public string ProviderName => "WindowsRegistry";

    public Task<ScannerMetadataProviderResult> GetMetadataAsync(AdapterScannerDevice scanner, CancellationToken cancellationToken) =>
        _inner.GetMetadataAsync(scanner, cancellationToken);

    private sealed class RegistryCatalogAdapter(IRegistryScannerMetadataCatalog catalog) : IPnpScannerMetadataCatalog
    {
        public Task<(bool IsAvailable, IReadOnlyList<WindowsScannerMetadataRecord> Records)> ReadAsync(CancellationToken cancellationToken) =>
            catalog.ReadAsync(cancellationToken);
    }
}

public sealed class WindowsPnpScannerMetadataCatalog : IPnpScannerMetadataCatalog
{
    private const string UsbRoot = @"SYSTEM\CurrentControlSet\Enum\USB";
    private const string DriverClassRoot = @"SYSTEM\CurrentControlSet\Control\Class";

    public Task<(bool IsAvailable, IReadOnlyList<WindowsScannerMetadataRecord> Records)> ReadAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<(bool, IReadOnlyList<WindowsScannerMetadataRecord>)>((false, []));
        }

        var records = new List<WindowsScannerMetadataRecord>();
        using var root = Registry.LocalMachine.OpenSubKey(UsbRoot, writable: false);
        if (root is null) return Task.FromResult<(bool, IReadOnlyList<WindowsScannerMetadataRecord>)>((false, []));
        foreach (var hardwareKeyName in root.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var hardwareKey = root.OpenSubKey(hardwareKeyName, writable: false);
            if (hardwareKey is null) continue;
            foreach (var instanceName in hardwareKey.GetSubKeyNames())
            {
                using var instance = hardwareKey.OpenSubKey(instanceName, writable: false);
                if (instance is null) continue;
                records.Add(ReadRecord(instance, $"USB\\{hardwareKeyName}\\{instanceName}"));
            }
        }

        return Task.FromResult<(bool, IReadOnlyList<WindowsScannerMetadataRecord>)>((true, records));
    }

    [SupportedOSPlatform("windows")]
    private static WindowsScannerMetadataRecord ReadRecord(RegistryKey key, string instanceId)
    {
        var driver = ReadDriver(key.GetValue("Driver")?.ToString());
        return new WindowsScannerMetadataRecord(
            instanceId,
            ReadStrings(key, "HardwareID"),
            ReadString(key, "ContainerID"),
            ReadStrings(key, "LocationPaths"),
            ReadString(key, "Mfg", "Manufacturer"),
            ReadString(key, "FriendlyName", "DeviceDesc"),
            driver.Name ?? ReadString(key, "DriverDesc", "DeviceDesc"),
            driver.Provider ?? ReadString(key, "ProviderName", "Mfg"),
            driver.Version ?? ReadString(key, "DriverVersion"))
        {
            FirmwareVersion = ReadString(key, "FirmwareVersion", "FirmwareRevision")
        };
    }

    [SupportedOSPlatform("windows")]
    private static (string? Name, string? Provider, string? Version) ReadDriver(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            !Regex.IsMatch(relativePath, @"^\{[0-9A-Fa-f-]{36}\}\\[0-9]{4}$", RegexOptions.CultureInvariant))
        {
            return default;
        }

        using var key = Registry.LocalMachine.OpenSubKey($@"{DriverClassRoot}\{relativePath}", writable: false);
        return key is null
            ? default
            : (ReadString(key, "DriverDesc"), ReadString(key, "ProviderName"), ReadString(key, "DriverVersion"));
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadString(RegistryKey key, params string[] names) =>
        names.Select(name => key.GetValue(name)?.ToString()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> ReadStrings(RegistryKey key, string name) => key.GetValue(name) switch
    {
        string[] values => values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
        string value when !string.IsNullOrWhiteSpace(value) => [value],
        _ => []
    };
}

public sealed class WindowsScannerRegistryMetadataCatalog : IRegistryScannerMetadataCatalog
{
    private const string StillImageRoot = @"SYSTEM\CurrentControlSet\Control\StillImage\Devices";

    public Task<(bool IsAvailable, IReadOnlyList<WindowsScannerMetadataRecord> Records)> ReadAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<(bool, IReadOnlyList<WindowsScannerMetadataRecord>)>((false, []));
        }

        var records = new List<WindowsScannerMetadataRecord>();
        using var root = Registry.LocalMachine.OpenSubKey(StillImageRoot, writable: false);
        if (root is null) return Task.FromResult<(bool, IReadOnlyList<WindowsScannerMetadataRecord>)>((false, []));
        foreach (var name in root.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var key = root.OpenSubKey(name, writable: false);
            if (key is null) continue;
            records.Add(new WindowsScannerMetadataRecord(
                key.GetValue("DeviceInstanceId")?.ToString() ?? name,
                ReadStrings(key, "HardwareID"),
                key.GetValue("ContainerID")?.ToString(),
                ReadStrings(key, "LocationPaths"),
                key.GetValue("Manufacturer")?.ToString(),
                key.GetValue("FriendlyName")?.ToString() ?? key.GetValue("DeviceName")?.ToString(),
                key.GetValue("DriverName")?.ToString(),
                key.GetValue("DriverProvider")?.ToString(),
                key.GetValue("DriverVersion")?.ToString())
            {
                FirmwareVersion = key.GetValue("FirmwareVersion")?.ToString() ?? key.GetValue("FirmwareRevision")?.ToString()
            });
        }
        return Task.FromResult<(bool, IReadOnlyList<WindowsScannerMetadataRecord>)>((true, records));
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> ReadStrings(RegistryKey key, string name) => key.GetValue(name) switch
    {
        string[] values => values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
        string value when !string.IsNullOrWhiteSpace(value) => [value],
        _ => []
    };
}

public interface IVendorScannerMetadataProvider : IScannerMetadataProvider
{
}
