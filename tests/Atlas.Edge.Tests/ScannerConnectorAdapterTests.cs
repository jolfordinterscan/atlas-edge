using Atlas.Edge.ScannerConnectors;
using Atlas.Edge.ScannerDiscovery;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Edge.Tests;

public sealed class ScannerConnectorAdapterTests
{
    [Fact]
    public async Task WiaConnector_ReusesDiscoveryAndHealthDataWithoutInventingValues()
    {
        var catalog = new StaticWiaCatalog(new ScannerSourceCatalogResult(
            true,
            [new ScannerSourceMetadata(
                "raw-source-id",
                "Acme",
                "ScanPro",
                "SERIAL-1",
                "1.2.3",
                "USB",
                true,
                null,
                true,
                ["duplex", "feeder"],
                new ScannerDriver("Acme Driver", "2.0", "Acme"),
                ScannerOnlineStatus.Online,
                new Dictionary<string, string>
                {
                    ["Lifetime Pages"] = "1200",
                    ["Jam Count"] = "2",
                    ["Roller Life"] = "75",
                    ["Driver Status"] = "Ready"
                })]));
        using var connector = new WiaScannerConnector(catalog);

        Assert.Equal(ConnectorResultState.Known, (await connector.CheckAvailabilityAsync(CancellationToken.None)).State);
        var target = Assert.Single((await connector.DiscoverAsync(CancellationToken.None)).Value);
        var identity = (await connector.ReadIdentityAsync(target, CancellationToken.None)).Value!;
        var capabilities = (await connector.ReadCapabilitiesAsync(target, CancellationToken.None)).Value!;
        var counters = (await connector.ReadCountersAsync(target, CancellationToken.None)).Value!;
        var health = (await connector.ReadHealthAsync(target, CancellationToken.None)).Value!;
        var status = (await connector.ReadCurrentStatusAsync(target, CancellationToken.None)).Value!;
        var logs = await connector.ReadLogReferencesAsync(target, CancellationToken.None);

        Assert.Equal("Acme", identity.Manufacturer.Value);
        Assert.Equal("SERIAL-1", identity.SerialNumber.Value);
        Assert.DoesNotContain("raw-source-id", target.TargetId, StringComparison.Ordinal);
        Assert.DoesNotContain("SERIAL-1", target.TargetId, StringComparison.Ordinal);
        Assert.Equal(ConnectorResultState.Unknown, capabilities.Color.State);
        Assert.Equal(1200, counters.LifetimePages.Value);
        Assert.Equal(ConnectorResultState.Unknown, counters.DailyPages.State);
        Assert.Equal(75m, health.RollerLifePercent.Value);
        Assert.Equal(ConnectorResultState.Unknown, health.PadLifePercent.State);
        Assert.Equal(ConnectorScannerOnlineStatus.Online, status.OnlineStatus.Value);
        Assert.Equal(ConnectorDriverStatus.Ready, status.DriverStatus.Value);
        Assert.Equal(ConnectorResultState.Unsupported, logs.State);
    }

    [Fact]
    public async Task WiaConnector_ReportsPlatformUnavailableWithStableCode()
    {
        using var connector = new WiaScannerConnector(
            new StaticWiaCatalog(new ScannerSourceCatalogResult(false, Array.Empty<ScannerSourceMetadata>())));

        var availability = await connector.CheckAvailabilityAsync(CancellationToken.None);
        var discovery = await connector.DiscoverAsync(CancellationToken.None);

        Assert.Equal(ConnectorResultState.Unavailable, availability.State);
        Assert.Equal("wia_runtime_unavailable", availability.ErrorCode);
        Assert.Equal(ConnectorResultState.Unavailable, discovery.State);
        Assert.Equal("wia_runtime_unavailable", discovery.ErrorCode);
    }

    [Fact]
    public async Task DevelopmentMockConnector_ResolvesThroughDependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IScannerConnector, DevelopmentMockScannerConnector>();
        services.AddSingleton<IScannerConnectorManager, ScannerConnectorManager>();
        await using var provider = services.BuildServiceProvider();

        var connector = Assert.IsType<DevelopmentMockScannerConnector>(
            provider.GetRequiredService<IScannerConnector>());
        var collection = await provider.GetRequiredService<IScannerConnectorManager>()
            .CollectAsync(CancellationToken.None);

        Assert.True(connector.Descriptor.DevelopmentOnly);
        Assert.Contains(ConnectorCapability.LogReferences, connector.Descriptor.Capabilities);
        var scanner = Assert.Single(collection.Scanners);
        Assert.Equal(ConnectorResultState.Known, scanner.LogReferences.State);
        Assert.Single(scanner.LogReferences.Value);
    }

    private sealed class StaticWiaCatalog : IWiaScannerSourceCatalog
    {
        private readonly ScannerSourceCatalogResult _result;

        public StaticWiaCatalog(ScannerSourceCatalogResult result) => _result = result;

        public Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }
}
