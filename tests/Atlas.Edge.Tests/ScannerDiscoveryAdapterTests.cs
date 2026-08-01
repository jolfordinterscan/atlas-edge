using Atlas.Edge.ScannerDiscovery;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Edge.Tests;

public sealed class ScannerDiscoveryAdapterTests
{
    [Theory]
    [InlineData(ScannerProtocol.Wia)]
    [InlineData(ScannerProtocol.Twain)]
    [InlineData(ScannerProtocol.Isis)]
    public async Task PlatformAdapter_MapsCompleteSourceMetadata(ScannerProtocol protocol)
    {
        var source = new ScannerSourceMetadata(
            "source-1",
            "Contoso",
            "DocumentScan 900",
            "SERIAL-900",
            "3.4.5",
            "USB",
            true,
            true,
            true,
            ["duplex", "color", "automatic-document-feeder"],
            new ScannerDriver("Contoso Driver", "7.8.9", "Contoso"),
            ScannerOnlineStatus.Online);

        var result = await CreateAdapter(protocol, new ScannerSourceCatalogResult(true, [source]))
            .DiscoverAsync(CancellationToken.None);

        var device = Assert.Single(result.Devices);
        Assert.True(result.IsAvailable);
        Assert.Equal(protocol, device.Protocol);
        Assert.Equal("Contoso", device.Manufacturer);
        Assert.Equal("DocumentScan 900", device.Model);
        Assert.Equal("SERIAL-900", device.SerialNumber);
        Assert.Equal("3.4.5", device.FirmwareVersion);
        Assert.Equal("USB", device.Interface);
        Assert.True(device.SupportsDuplex);
        Assert.True(device.SupportsColor);
        Assert.True(device.HasFeeder);
        Assert.Equal(ScannerOnlineStatus.Online, device.OnlineStatus);
        Assert.Equal("Contoso Driver", device.Driver.Name);
    }

    [Theory]
    [InlineData(ScannerProtocol.Wia, "wia_runtime_unavailable")]
    [InlineData(ScannerProtocol.Twain, "twain_runtime_unavailable")]
    [InlineData(ScannerProtocol.Isis, "isis_runtime_unavailable")]
    public async Task PlatformAdapter_ReportsUnavailableRuntimeWithStableCode(
        ScannerProtocol protocol,
        string expectedCode)
    {
        var result = await CreateAdapter(
                protocol,
                new ScannerSourceCatalogResult(false, Array.Empty<ScannerSourceMetadata>()))
            .DiscoverAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Devices);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    [Fact]
    public async Task MockProvider_ReturnsObviousLocalDevelopmentScanner()
    {
        var result = await new MockScannerDiscoveryAdapter().DiscoverAsync(CancellationToken.None);

        var scanner = Assert.Single(result.Devices);
        Assert.Equal(ScannerProtocol.Mock, scanner.Protocol);
        Assert.Contains("Mock", scanner.Manufacturer, StringComparison.Ordinal);
        Assert.Equal(ScannerOnlineStatus.Online, scanner.OnlineStatus);
        Assert.True(scanner.SupportsDuplex);
        Assert.True(scanner.SupportsColor);
        Assert.True(scanner.HasFeeder);
    }

    [Fact]
    public async Task MockProvider_DependencyInjectionUsesDefaultInventory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IScannerDiscoveryAdapter, MockScannerDiscoveryAdapter>();
        using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IScannerDiscoveryAdapter>();
        var result = await adapter.DiscoverAsync(CancellationToken.None);

        Assert.Single(result.Devices);
    }

    [Fact]
    public async Task NativeCatalogs_AreUnavailableOutsideWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.False((await new WiaScannerSourceCatalog().EnumerateAsync(CancellationToken.None)).IsAvailable);
        Assert.False((await new TwainScannerSourceCatalog().EnumerateAsync(CancellationToken.None)).IsAvailable);
        Assert.False((await new IsisScannerSourceCatalog().EnumerateAsync(CancellationToken.None)).IsAvailable);
    }

    private static IScannerDiscoveryAdapter CreateAdapter(
        ScannerProtocol protocol,
        ScannerSourceCatalogResult result) =>
        protocol switch
        {
            ScannerProtocol.Wia => new WiaScannerDiscoveryAdapter(new WiaCatalog(result)),
            ScannerProtocol.Twain => new TwainScannerDiscoveryAdapter(new TwainCatalog(result)),
            ScannerProtocol.Isis => new IsisScannerDiscoveryAdapter(new IsisCatalog(result)),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, null)
        };

    private sealed class WiaCatalog : IWiaScannerSourceCatalog
    {
        private readonly ScannerSourceCatalogResult _result;

        public WiaCatalog(ScannerSourceCatalogResult result) => _result = result;

        public Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }

    private sealed class TwainCatalog : ITwainScannerSourceCatalog
    {
        private readonly ScannerSourceCatalogResult _result;

        public TwainCatalog(ScannerSourceCatalogResult result) => _result = result;

        public Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }

    private sealed class IsisCatalog : IIsisScannerSourceCatalog
    {
        private readonly ScannerSourceCatalogResult _result;

        public IsisCatalog(ScannerSourceCatalogResult result) => _result = result;

        public Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }
}
