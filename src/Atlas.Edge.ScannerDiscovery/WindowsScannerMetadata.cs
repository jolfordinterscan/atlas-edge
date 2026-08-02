using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
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

    public string? Service { get; init; }
}

public static partial class ScannerMetadataNameNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        var resourceSuffix = trimmed.LastIndexOf(';') is var separator && separator >= 0 && separator < trimmed.Length - 1
            ? trimmed[(separator + 1)..].Trim()
            : trimmed;
        var withoutDriverSuffix = DriverSuffixRegex().Replace(resourceSuffix, string.Empty);
        return WhitespaceRegex().Replace(withoutDriverSuffix, " ").ToUpperInvariant();
    }

    public static bool ManufacturerMatches(string? left, string? right) =>
        Normalize(left) is { Length: > 0 } normalized && normalized == Normalize(right);

    public static bool ModelMatches(
        string? scannerModel,
        string? candidateName,
        string? scannerManufacturer,
        string? candidateManufacturer)
    {
        var left = RemoveManufacturerPrefix(Normalize(scannerModel), Normalize(scannerManufacturer));
        var right = RemoveManufacturerPrefix(Normalize(candidateName), Normalize(candidateManufacturer));
        return left.Length > 0 && left == right;
    }

    private static string RemoveManufacturerPrefix(string value, string manufacturer)
    {
        if (manufacturer.Length == 0 || value == manufacturer) return value;
        var prefix = manufacturer + " ";
        return value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
    }

    [GeneratedRegex(@"\s+#\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex DriverSuffixRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
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
            var diagnostics = scanner.MetadataDiagnostics.ToList();
            foreach (var provider in _providers)
            {
                try
                {
                    var result = await provider.GetMetadataAsync(scanner, cancellationToken)
                        .WaitAsync(_providerTimeout, _timeProvider, cancellationToken);
                    if (result.Diagnostic is not null)
                    {
                        diagnostics.Add(result.Diagnostic);
                    }
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
                EnrichedMetadata = metadata,
                MetadataDiagnostics = diagnostics.ToArray()
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
    private const int ExactInstanceScore = 400;
    private const int ExactVidPidScore = 300;
    private const int ManufacturerModelScore = 200;
    private const int ManufacturerScannerClassScore = 100;
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
            return ScannerMetadataProviderResult.Unavailable("pnp_unavailable") with
            {
                Diagnostic = Diagnostic("Unavailable", 0, 0, false, null)
            };
        }

        var match = Match(scanner, result.Records);
        if (match.Record is null)
        {
            return ScannerMetadataProviderResult.Available(null) with
            {
                Diagnostic = Diagnostic(
                    match.Strategy,
                    match.Score,
                    result.Records.Count,
                    match.IsAmbiguous,
                    null)
            };
        }

        var metadata = Normalize(match.Record);
        return ScannerMetadataProviderResult.Available(metadata) with
        {
            Diagnostic = Diagnostic(
                match.Strategy,
                match.Score,
                result.Records.Count,
                false,
                metadata)
        };
    }

    private static MetadataMatch Match(
        AdapterScannerDevice scanner,
        IReadOnlyList<WindowsScannerMetadataRecord> records)
    {
        var identity = $"{scanner.SourceId}|{scanner.DevicePath}";
        var exactInstance = records.Where(record =>
            !string.IsNullOrWhiteSpace(record.DeviceInstanceId) &&
            identity.Contains(record.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
        var resolved = Resolve(exactInstance, "ExactDeviceInstance", ExactInstanceScore);
        if (resolved.HasCandidates) return resolved;

        var scannerUsbIds = ExtractUsbIds(identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (scannerUsbIds.Any(value => value.StartsWith("VID_", StringComparison.OrdinalIgnoreCase)) &&
            scannerUsbIds.Any(value => value.StartsWith("PID_", StringComparison.OrdinalIgnoreCase)))
        {
            var exactUsb = records.Where(record =>
                ExtractUsbIds($"{record.DeviceInstanceId}|{string.Join('|', record.HardwareIds)}")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .IsSupersetOf(scannerUsbIds));
            resolved = Resolve(exactUsb, "ExactVidPid", ExactVidPidScore);
            if (resolved.HasCandidates) return resolved;
        }

        var manufacturerModel = records.Where(record =>
            ScannerMetadataNameNormalizer.ManufacturerMatches(scanner.Manufacturer, record.Manufacturer) &&
            new[] { record.FriendlyName, record.DriverName }.Any(candidate =>
                ScannerMetadataNameNormalizer.ModelMatches(
                    scanner.Model,
                    candidate,
                    scanner.Manufacturer,
                    record.Manufacturer)));
        resolved = Resolve(manufacturerModel, "ManufacturerModelUnique", ManufacturerModelScore);
        if (resolved.HasCandidates) return resolved;

        var manufacturerScannerClass = records.Where(record =>
            ScannerMetadataNameNormalizer.ManufacturerMatches(scanner.Manufacturer, record.Manufacturer) &&
            string.Equals(record.Service?.Trim(), "usbscan", StringComparison.OrdinalIgnoreCase));
        resolved = Resolve(manufacturerScannerClass, "ManufacturerScannerClassUnique", ManufacturerScannerClassScore);
        return resolved.HasCandidates ? resolved : new MetadataMatch(null, "None", 0, false, false);
    }

    private static MetadataMatch Resolve(
        IEnumerable<WindowsScannerMetadataRecord> candidates,
        string strategy,
        int score)
    {
        var matches = candidates.ToArray();
        return matches.Length switch
        {
            0 => new MetadataMatch(null, strategy, score, false, false),
            1 => new MetadataMatch(matches[0], strategy, score, false, true),
            _ => new MetadataMatch(null, strategy, score, true, true)
        };
    }

    private ScannerMetadataMatchDiagnostic Diagnostic(
        string strategy,
        int score,
        int candidates,
        bool ambiguous,
        ScannerMetadata? metadata) =>
        new(
            ProviderName,
            strategy,
            score,
            candidates,
            ambiguous,
            metadata is null ? [] : PopulatedFields(metadata));

    private static IReadOnlyList<string> PopulatedFields(ScannerMetadata metadata) =>
        new (string Name, string? Value)[]
        {
            ("SerialNumber", metadata.SerialNumber),
            ("HardwareIdHash", metadata.HardwareId),
            ("DriverName", metadata.DriverName),
            ("DriverProvider", metadata.DriverProvider),
            ("DriverVersion", metadata.DriverVersion),
            ("UsbVendorId", metadata.UsbVendorId),
            ("UsbProductId", metadata.UsbProductId),
            ("ContainerIdHash", metadata.ContainerId),
            ("LocationPathHash", metadata.LocationPathHash),
            ("FriendlyName", metadata.FriendlyName),
            ("DeviceInstanceIdHash", metadata.DeviceInstanceIdHash),
            ("FirmwareVersion", metadata.FirmwareVersion)
        }.Where(field => !string.IsNullOrWhiteSpace(field.Value)).Select(field => field.Name).ToArray();

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
        return string.IsNullOrWhiteSpace(candidate) ||
            candidate.Contains('&') ||
            !Regex.IsMatch(candidate, @"^[A-Za-z0-9][A-Za-z0-9._-]{3,127}$", RegexOptions.CultureInvariant)
                ? null
                : Bound(candidate);
    }

    private static string? Bound(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 256)];

    private sealed record MetadataMatch(
        WindowsScannerMetadataRecord? Record,
        string Strategy,
        int Score,
        bool IsAmbiguous,
        bool HasCandidates);
}

public sealed class WindowsRegistryScannerMetadataProvider : IRegistryScannerMetadataProvider
{
    private readonly WindowsPnpScannerMetadataProvider _inner;

    public WindowsRegistryScannerMetadataProvider(IRegistryScannerMetadataCatalog catalog) =>
        _inner = new WindowsPnpScannerMetadataProvider(new RegistryCatalogAdapter(catalog));

    public string ProviderName => "WindowsRegistry";

    public async Task<ScannerMetadataProviderResult> GetMetadataAsync(
        AdapterScannerDevice scanner,
        CancellationToken cancellationToken)
    {
        var result = await _inner.GetMetadataAsync(scanner, cancellationToken);
        return result.Diagnostic is null
            ? result
            : result with { Diagnostic = result.Diagnostic with { ProviderName = ProviderName } };
    }

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
        var properties = WindowsPnpDevicePropertyReader.Read(instanceId);
        var driver = ReadDriver(key.GetValue("Driver")?.ToString());
        return new WindowsScannerMetadataRecord(
            instanceId,
            FirstValues(properties.HardwareIds, ReadStrings(key, "HardwareID")),
            properties.ContainerId ?? ReadString(key, "ContainerID"),
            FirstValues(properties.LocationPaths, ReadStrings(key, "LocationPaths")),
            properties.Manufacturer ?? ReadString(key, "Mfg", "Manufacturer"),
            properties.FriendlyName ?? ReadString(key, "FriendlyName", "DeviceDesc"),
            properties.DriverName ?? driver.Name ?? ReadString(key, "DriverDesc", "DeviceDesc"),
            properties.DriverProvider ?? driver.Provider ?? ReadString(key, "ProviderName", "Mfg"),
            properties.DriverVersion ?? driver.Version ?? ReadString(key, "DriverVersion"))
        {
            FirmwareVersion = ReadString(key, "FirmwareVersion", "FirmwareRevision"),
            Service = properties.Service ?? ReadString(key, "Service")
        };
    }

    private static IReadOnlyList<string> FirstValues(
        IReadOnlyList<string> preferred,
        IReadOnlyList<string> fallback) => preferred.Count > 0 ? preferred : fallback;

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

[SupportedOSPlatform("windows")]
internal static class WindowsPnpDevicePropertyReader
{
    private const int Success = 0;
    private const int BufferSmall = 0x1A;
    private const int MaximumPropertyBytes = 64 * 1024;
    private static readonly DevicePropertyKey HardwareIds = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 3);
    private static readonly DevicePropertyKey Service = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 6);
    private static readonly DevicePropertyKey Manufacturer = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 13);
    private static readonly DevicePropertyKey FriendlyName = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static readonly DevicePropertyKey LocationPaths = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 37);
    private static readonly DevicePropertyKey ContainerId = new(
        new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    private static readonly DevicePropertyKey DriverName = new(
        new Guid("A8B865DD-2E3D-4094-AD97-E593A70C75D6"), 4);
    private static readonly DevicePropertyKey DriverVersion = new(
        new Guid("A8B865DD-2E3D-4094-AD97-E593A70C75D6"), 3);
    private static readonly DevicePropertyKey DriverProvider = new(
        new Guid("A8B865DD-2E3D-4094-AD97-E593A70C75D6"), 9);

    public static PnpDeviceProperties Read(string deviceInstanceId)
    {
        try
        {
            if (LocateDeviceNode(out var deviceNode, deviceInstanceId, 0) != Success)
            {
                return PnpDeviceProperties.Empty;
            }

            return new PnpDeviceProperties(
                ReadStrings(deviceNode, HardwareIds),
                ReadGuid(deviceNode, ContainerId),
                ReadStrings(deviceNode, LocationPaths),
                ReadString(deviceNode, Manufacturer),
                ReadString(deviceNode, FriendlyName),
                ReadString(deviceNode, Service),
                ReadString(deviceNode, DriverName),
                ReadString(deviceNode, DriverProvider),
                ReadString(deviceNode, DriverVersion));
        }
        catch (Exception)
        {
            return PnpDeviceProperties.Empty;
        }
    }

    private static string? ReadString(uint deviceNode, DevicePropertyKey key) =>
        ReadBytes(deviceNode, key) is { Length: > 1 } bytes
            ? Encoding.Unicode.GetString(bytes).TrimEnd('\0').Trim()
            : null;

    private static IReadOnlyList<string> ReadStrings(uint deviceNode, DevicePropertyKey key) =>
        ReadBytes(deviceNode, key) is { Length: > 1 } bytes
            ? Encoding.Unicode.GetString(bytes)
                .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

    private static string? ReadGuid(uint deviceNode, DevicePropertyKey key)
    {
        var bytes = ReadBytes(deviceNode, key);
        return bytes is { Length: >= 16 } ? new Guid(bytes.AsSpan(0, 16)).ToString("B") : null;
    }

    private static byte[]? ReadBytes(uint deviceNode, DevicePropertyKey key)
    {
        uint propertyType;
        uint size = 0;
        var propertyKey = key;
        var result = GetDeviceNodeProperty(deviceNode, ref propertyKey, out propertyType, IntPtr.Zero, ref size, 0);
        if (result != BufferSmall || size == 0 || size > MaximumPropertyBytes)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            result = GetDeviceNodeProperty(deviceNode, ref propertyKey, out propertyType, buffer, ref size, 0);
            if (result != Success) return null;
            var bytes = new byte[size];
            Marshal.Copy(buffer, bytes, 0, checked((int)size));
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode)]
    private static extern int LocateDeviceNode(out uint deviceNode, string deviceInstanceId, uint flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    private static extern int GetDeviceNodeProperty(
        uint deviceNode,
        ref DevicePropertyKey propertyKey,
        out uint propertyType,
        IntPtr propertyBuffer,
        ref uint propertyBufferSize,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct DevicePropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }
}

internal sealed record PnpDeviceProperties(
    IReadOnlyList<string> HardwareIds,
    string? ContainerId,
    IReadOnlyList<string> LocationPaths,
    string? Manufacturer,
    string? FriendlyName,
    string? Service,
    string? DriverName,
    string? DriverProvider,
    string? DriverVersion)
{
    public static PnpDeviceProperties Empty { get; } =
        new([], null, [], null, null, null, null, null, null);
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
                FirmwareVersion = key.GetValue("FirmwareVersion")?.ToString() ?? key.GetValue("FirmwareRevision")?.ToString(),
                Service = key.GetValue("Service")?.ToString()
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
