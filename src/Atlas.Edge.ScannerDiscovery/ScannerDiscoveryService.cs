using Microsoft.Extensions.Logging;

namespace Atlas.Edge.ScannerDiscovery;

public sealed class ScannerDiscoveryService : IScannerDiscoveryService
{
    private readonly IReadOnlyList<IScannerDiscoveryAdapter> _adapters;
    private readonly ILogger<ScannerDiscoveryService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IScannerIdentityFactory _identityFactory;
    private readonly IScannerMetadataEnricher? _metadataEnricher;
    private readonly TimeSpan _providerTimeout;
    private readonly Dictionary<string, DateTimeOffset> _firstObserved = new(StringComparer.Ordinal);

    public ScannerDiscoveryService(
        IEnumerable<IScannerDiscoveryAdapter> adapters,
        TimeProvider timeProvider,
        ILogger<ScannerDiscoveryService> logger)
        : this(adapters, timeProvider, logger, new ScannerIdentityFactory(), TimeSpan.FromSeconds(15))
    {
    }

    public ScannerDiscoveryService(
        IEnumerable<IScannerDiscoveryAdapter> adapters,
        TimeProvider timeProvider,
        ILogger<ScannerDiscoveryService> logger,
        IScannerIdentityFactory identityFactory,
        TimeSpan providerTimeout,
        IScannerMetadataEnricher? metadataEnricher = null)
    {
        _adapters = adapters.ToArray();
        _timeProvider = timeProvider;
        _logger = logger;
        _identityFactory = identityFactory;
        _metadataEnricher = metadataEnricher;
        _providerTimeout = providerTimeout > TimeSpan.Zero
            ? providerTimeout
            : throw new ArgumentOutOfRangeException(nameof(providerTimeout));
    }

    public async Task<ScannerDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
    {
        var devices = new List<AdapterScannerDevice>();
        var diagnostics = new List<ScannerAdapterDiagnostic>();

        foreach (var adapter in _adapters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ScannerAdapterResult result;
            var started = _timeProvider.GetTimestamp();
            try
            {
                result = await adapter.DiscoverAsync(cancellationToken)
                    .WaitAsync(_providerTimeout, _timeProvider, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                result = ScannerAdapterResult.Failed(adapter.Protocol, "provider_timeout");
                _logger.LogWarning(
                    "Scanner discovery adapter {Protocol} timed out; other adapters will continue.",
                    adapter.Protocol);
            }
            catch (Exception)
            {
                result = ScannerAdapterResult.Failed(adapter.Protocol, "adapter_failure");
                _logger.LogWarning(
                    "Scanner discovery adapter {Protocol} failed; other adapters will continue.",
                    adapter.Protocol);
            }

            devices.AddRange(result.Devices);
            diagnostics.Add(new ScannerAdapterDiagnostic(
                result.Protocol,
                result.IsAvailable,
                result.Devices.Count,
                result.ErrorCode)
            {
                CollectionDuration = _timeProvider.GetElapsedTime(started),
                CollectedAtUtc = _timeProvider.GetUtcNow(),
                Warnings = result.Warnings.ToArray()
            });
        }

        var enrichedDevices = _metadataEnricher is null
            ? devices
            : (await _metadataEnricher.EnrichAsync(devices, cancellationToken)).ToList();
        var observedAt = _timeProvider.GetUtcNow();
        return new ScannerDiscoverySnapshot(
            observedAt,
            MergeDevices(enrichedDevices, observedAt),
            diagnostics.OrderBy(diagnostic => diagnostic.Protocol).ToArray());
    }

    private IReadOnlyList<DiscoveredScanner> MergeDevices(
        IEnumerable<AdapterScannerDevice> devices,
        DateTimeOffset observedAt) =>
        devices
            .GroupBy(CreateMergeKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateScanner(group, observedAt))
            .OrderBy(scanner => scanner.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(scanner => scanner.Model, StringComparer.OrdinalIgnoreCase)
            .ThenBy(scanner => scanner.DiscoveryId, StringComparer.Ordinal)
            .ToArray();

    private static string CreateMergeKey(AdapterScannerDevice device)
    {
        if (!string.IsNullOrWhiteSpace(device.SerialNumber))
        {
            return $"serial|{Normalize(device.Manufacturer)}|{Normalize(device.SerialNumber)}";
        }

        return $"source|{Normalize(device.Manufacturer)}|{Normalize(device.Model)}|{Normalize(device.SourceId)}";
    }

    private DiscoveredScanner CreateScanner(
        IGrouping<string, AdapterScannerDevice> group,
        DateTimeOffset observedAt)
    {
        var devices = group.ToArray();
        var preferred = devices
            .OrderByDescending(device => device.OnlineStatus == ScannerOnlineStatus.Online)
            .ThenByDescending(device => device.Protocol == ScannerProtocol.Wia)
            .First();

        var identity = _identityFactory.Create(preferred);
        var metadata = preferred.EnrichedMetadata;
        var scannerId = identity.ScannerId;
        if (!_firstObserved.TryGetValue(scannerId, out var firstObserved))
        {
            firstObserved = observedAt;
            _firstObserved[scannerId] = observedAt;
        }

        var capabilities = devices.SelectMany(device => device.Capabilities)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Bound)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var onlineStatus = MergeOnlineStatus(devices.Select(device => device.OnlineStatus), preferred.OnlineStatus);

        return new DiscoveredScanner(
            scannerId,
            Bound(FirstValue(devices.Select(device => device.Manufacturer)) ?? "Unknown"),
            Bound(FirstValue(devices.Select(device => device.Model)) ?? "Unknown"),
            BoundNullable(FirstValue(devices.Select(device => device.SerialNumber))),
            BoundNullable(FirstValue(devices.Select(device => device.FirmwareVersion))),
            FirstKnownInterface(devices.Select(device => device.Interface)),
            MergeBoolean(devices.Select(device => device.SupportsDuplex)),
            MergeBoolean(devices.Select(device => device.SupportsColor)),
            MergeBoolean(devices.Select(device => device.HasFeeder)),
            capabilities,
            devices.Select(device => device.Driver)
                .Distinct()
                .ToArray(),
            onlineStatus,
            devices.Select(device => device.Protocol)
                .Distinct()
                .OrderBy(protocol => protocol)
                .ToArray())
        {
            ProviderId = identity.ProviderId,
            ProviderName = preferred.Protocol.ToString(),
            DevicePathHash = identity.DevicePathHash,
            Status = ToOperationalStatus(onlineStatus),
            ConnectionType = ToConnectionType(preferred.Interface),
            MetadataConfidence = identity.Confidence,
            FirstObservedUtc = firstObserved,
            LastObservedUtc = observedAt,
            NormalizedCapabilities = NormalizeCapabilities(capabilities),
            DiscoveryWarnings = [],
            SerialSource = metadata?.SerialSource,
            HardwareId = metadata?.HardwareId,
            DriverProvider = FirstValue(devices.Select(device => device.EnrichedMetadata?.DriverProvider)) ??
                FirstValue(devices.Select(device => device.Driver.Provider)),
            UsbVendorId = metadata?.UsbVendorId,
            UsbProductId = metadata?.UsbProductId,
            ContainerId = metadata?.ContainerId,
            LocationPathHash = metadata?.LocationPathHash,
            FriendlyName = metadata?.FriendlyName,
            DeviceInstanceIdHash = metadata?.DeviceInstanceIdHash
        };
    }

    private static string? FirstValue(IEnumerable<string?> values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string FirstKnownInterface(IEnumerable<string> values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase))?.Trim() ?? "Unknown";

    private static bool? MergeBoolean(IEnumerable<bool?> values)
    {
        var known = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return known.Length == 0 ? null : known.Any(value => value);
    }

    private static ScannerOnlineStatus MergeOnlineStatus(
        IEnumerable<ScannerOnlineStatus> values,
        ScannerOnlineStatus fallback)
    {
        var statuses = values.ToArray();
        if (statuses.Contains(ScannerOnlineStatus.Online))
        {
            return ScannerOnlineStatus.Online;
        }

        if (statuses.Length > 0 && statuses.All(status => status == ScannerOnlineStatus.Offline))
        {
            return ScannerOnlineStatus.Offline;
        }

        return fallback;
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToUpperInvariant();

    private static string Bound(string value) => value.Trim()[..Math.Min(value.Trim().Length, 256)];

    private static string? BoundNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Bound(value);

    private static ScannerOperationalStatus ToOperationalStatus(ScannerOnlineStatus status) => status switch
    {
        ScannerOnlineStatus.Offline => ScannerOperationalStatus.Offline,
        _ => ScannerOperationalStatus.Unknown
    };

    private static ScannerConnectionType ToConnectionType(string value) => value.Trim().ToUpperInvariant() switch
    {
        "USB" => ScannerConnectionType.Usb,
        "SCSI" => ScannerConnectionType.Scsi,
        "NETWORK" => ScannerConnectionType.Network,
        _ => ScannerConnectionType.Unknown
    };

    private static IReadOnlyList<ScannerCapability> NormalizeCapabilities(IEnumerable<string> values) =>
        values.Select(value => value.Trim().ToLowerInvariant() switch
            {
                "automatic-document-feeder" or "adf" => ScannerCapability.AutomaticDocumentFeeder,
                "flatbed" => ScannerCapability.Flatbed,
                "duplex" => ScannerCapability.Duplex,
                "color" => ScannerCapability.Color,
                "grayscale" => ScannerCapability.Grayscale,
                "black-and-white" => ScannerCapability.BlackAndWhite,
                "multi-page" => ScannerCapability.MultiPage,
                _ => ScannerCapability.Unknown
            })
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
}
