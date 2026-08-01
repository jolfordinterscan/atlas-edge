namespace Atlas.Edge.ScannerDiscovery;

public sealed class WiaScannerDiscoveryAdapter : IScannerDiscoveryAdapter
{
    private readonly IWiaScannerSourceCatalog _catalog;

    public WiaScannerDiscoveryAdapter(IWiaScannerSourceCatalog catalog)
    {
        _catalog = catalog;
    }

    public ScannerProtocol Protocol => ScannerProtocol.Wia;

    public async Task<ScannerAdapterResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        var result = await _catalog.EnumerateAsync(cancellationToken);
        return result.IsAvailable
            ? ScannerAdapterResult.Available(Protocol, Map(result.Sources, Protocol))
            : ScannerAdapterResult.Unavailable(Protocol, "wia_runtime_unavailable");
    }

    private static IReadOnlyList<AdapterScannerDevice> Map(
        IEnumerable<ScannerSourceMetadata> sources,
        ScannerProtocol protocol) =>
        sources.Select(source => new AdapterScannerDevice(
            source.SourceId,
            protocol,
            source.Manufacturer,
            source.Model,
            source.SerialNumber,
            source.FirmwareVersion,
            source.Interface,
            source.SupportsDuplex,
            source.SupportsColor,
            source.HasFeeder,
            source.Capabilities,
            source.Driver,
            source.OnlineStatus)
        {
            DevicePath = source.DevicePath,
            HasProviderStableIdentity = source.HasProviderStableIdentity
        }).ToArray();
}

public sealed class TwainScannerDiscoveryAdapter : IScannerDiscoveryAdapter
{
    private readonly ITwainScannerSourceCatalog _catalog;

    public TwainScannerDiscoveryAdapter(ITwainScannerSourceCatalog catalog)
    {
        _catalog = catalog;
    }

    public ScannerProtocol Protocol => ScannerProtocol.Twain;

    public async Task<ScannerAdapterResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        var result = await _catalog.EnumerateAsync(cancellationToken);
        return result.IsAvailable
            ? ScannerAdapterResult.Available(Protocol, Map(result.Sources))
            : ScannerAdapterResult.Unavailable(Protocol, "twain_runtime_unavailable");
    }

    private static IReadOnlyList<AdapterScannerDevice> Map(IEnumerable<ScannerSourceMetadata> sources) =>
        sources.Select(source => new AdapterScannerDevice(
            source.SourceId,
            ScannerProtocol.Twain,
            source.Manufacturer,
            source.Model,
            source.SerialNumber,
            source.FirmwareVersion,
            source.Interface,
            source.SupportsDuplex,
            source.SupportsColor,
            source.HasFeeder,
            source.Capabilities,
            source.Driver,
            source.OnlineStatus)
        {
            DevicePath = source.DevicePath,
            HasProviderStableIdentity = source.HasProviderStableIdentity
        }).ToArray();
}

public sealed class IsisScannerDiscoveryAdapter : IScannerDiscoveryAdapter
{
    private readonly IIsisScannerSourceCatalog _catalog;

    public IsisScannerDiscoveryAdapter(IIsisScannerSourceCatalog catalog)
    {
        _catalog = catalog;
    }

    public ScannerProtocol Protocol => ScannerProtocol.Isis;

    public async Task<ScannerAdapterResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        var result = await _catalog.EnumerateAsync(cancellationToken);
        return result.IsAvailable
            ? ScannerAdapterResult.Available(Protocol, Map(result.Sources))
            : ScannerAdapterResult.Unavailable(Protocol, "isis_runtime_unavailable");
    }

    private static IReadOnlyList<AdapterScannerDevice> Map(IEnumerable<ScannerSourceMetadata> sources) =>
        sources.Select(source => new AdapterScannerDevice(
            source.SourceId,
            ScannerProtocol.Isis,
            source.Manufacturer,
            source.Model,
            source.SerialNumber,
            source.FirmwareVersion,
            source.Interface,
            source.SupportsDuplex,
            source.SupportsColor,
            source.HasFeeder,
            source.Capabilities,
            source.Driver,
            source.OnlineStatus)
        {
            DevicePath = source.DevicePath,
            HasProviderStableIdentity = source.HasProviderStableIdentity
        }).ToArray();
}

public sealed class MockScannerDiscoveryAdapter : IScannerDiscoveryAdapter
{
    private readonly IReadOnlyList<AdapterScannerDevice> _devices;

    public MockScannerDiscoveryAdapter()
        : this(CreateDefaultDevices())
    {
    }

    public MockScannerDiscoveryAdapter(IReadOnlyList<AdapterScannerDevice> devices)
    {
        _devices = devices.ToArray();
    }

    public ScannerProtocol Protocol => ScannerProtocol.Mock;

    public Task<ScannerAdapterResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ScannerAdapterResult.Available(Protocol, _devices));
    }

    private static IReadOnlyList<AdapterScannerDevice> CreateDefaultDevices() =>
        [
            new AdapterScannerDevice(
                "mock-scanner-001",
                ScannerProtocol.Mock,
                "Atlas Mock Devices",
                "Document Scanner",
                "MOCK-SERIAL-001",
                "0.0-mock",
                "USB",
                true,
                true,
                true,
                ["flatbed", "automatic-document-feeder", "duplex", "color"],
                new ScannerDriver("Atlas Mock Scanner Driver", "0.0-mock", "Atlas local development tooling"),
                ScannerOnlineStatus.Online)
        ];
}
