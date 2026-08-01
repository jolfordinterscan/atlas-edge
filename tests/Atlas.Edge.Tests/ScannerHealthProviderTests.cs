using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.ScannerHealth;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Edge.Tests;

public sealed class ScannerHealthProviderTests
{
    [Fact]
    public async Task WiaProvider_MapsOnlyExplicitDriverMetadata()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Lifetime Pages"] = "125000",
            ["Daily Pages"] = "450",
            ["Roller Life"] = "72.5",
            ["Pad Remaining"] = "64",
            ["Consumable.feed-roller"] = "72|ready",
            ["Maintenance.cleaning-cycles"] = "12",
            ["Scan Speed"] = "58",
            ["Rated PPM"] = "60",
            ["Jam Count"] = "2",
            ["Double Feed Count"] = "1",
            ["Transport Errors"] = "3",
            ["Driver Status"] = "ready",
            ["USB Disconnects"] = "1",
            ["USB Last Disconnect"] = "2026-07-31T10:00:00Z",
            ["Device Uptime Seconds"] = "3600"
        };
        var provider = new WiaScannerHealthProvider(new WiaCatalog(Result(true, metadata)));

        var result = await provider.CollectAsync(CancellationToken.None);

        var reading = Assert.Single(result.Readings);
        Assert.Equal(125000, reading.LifetimePages);
        Assert.Equal(450, reading.DailyPages);
        Assert.Equal(72.5m, reading.RollerLifePercent);
        Assert.Equal(64m, reading.PadLifePercent);
        Assert.True(reading.ConsumablesKnown);
        Assert.Equal(72m, Assert.Single(reading.Consumables).RemainingPercent);
        Assert.True(reading.MaintenanceCountersKnown);
        Assert.Equal(12, reading.MaintenanceCounters["cleaning-cycles"]);
        Assert.Equal("1.2.3", reading.FirmwareVersion);
        Assert.Equal(58m, reading.ScanSpeedPagesPerMinute);
        Assert.Equal(60m, reading.RatedScanSpeedPagesPerMinute);
        Assert.Equal(2, reading.JamCount);
        Assert.Equal(1, reading.DoubleFeedCount);
        Assert.Equal(3, reading.TransportErrorCount);
        Assert.Equal(ScannerOnlineStatus.Online, reading.OnlineStatus);
        Assert.Equal(ScannerDriverHealthStatus.Ready, reading.DriverStatus);
        Assert.Equal(1, reading.UsbStability!.DisconnectCount);
        Assert.Equal(TimeSpan.FromHours(1), reading.DeviceUptime);
    }

    [Fact]
    public async Task Provider_PreservesUnknownAndRejectsInvalidValues()
    {
        var metadata = new Dictionary<string, string>
        {
            ["Lifetime Pages"] = "not-a-number",
            ["Roller Life"] = "101",
            ["Jam Count"] = "-1",
            ["Driver Status"] = "vendor-specific"
        };
        var provider = new WiaScannerHealthProvider(new WiaCatalog(Result(true, metadata)));

        var reading = Assert.Single((await provider.CollectAsync(CancellationToken.None)).Readings);

        Assert.Null(reading.LifetimePages);
        Assert.Null(reading.DailyPages);
        Assert.Null(reading.RollerLifePercent);
        Assert.Null(reading.JamCount);
        Assert.False(reading.ConsumablesKnown);
        Assert.False(reading.MaintenanceCountersKnown);
        Assert.Equal(ScannerDriverHealthStatus.Unknown, reading.DriverStatus);
        Assert.Null(reading.UsbStability);
        Assert.Null(reading.DeviceUptime);
    }

    [Fact]
    public async Task Provider_DoesNotTreatRatedSpeedAsMeasuredSpeed()
    {
        var metadata = new Dictionary<string, string>
        {
            ["Rated Scan Speed"] = "60"
        };
        var provider = new WiaScannerHealthProvider(new WiaCatalog(Result(true, metadata)));

        var reading = Assert.Single((await provider.CollectAsync(CancellationToken.None)).Readings);

        Assert.Null(reading.ScanSpeedPagesPerMinute);
        Assert.Equal(60m, reading.RatedScanSpeedPagesPerMinute);
    }

    [Theory]
    [InlineData(ScannerProtocol.Wia, "wia_runtime_unavailable")]
    [InlineData(ScannerProtocol.Twain, "twain_runtime_unavailable")]
    [InlineData(ScannerProtocol.Isis, "isis_runtime_unavailable")]
    public async Task PlatformProvider_ReportsUnavailableRuntime(
        ScannerProtocol protocol,
        string expectedError)
    {
        IScannerHealthProvider provider = protocol switch
        {
            ScannerProtocol.Wia => new WiaScannerHealthProvider(new WiaCatalog(Result(false))),
            ScannerProtocol.Twain => new TwainScannerHealthProvider(new TwainCatalog(Result(false))),
            ScannerProtocol.Isis => new IsisScannerHealthProvider(new IsisCatalog(Result(false))),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol))
        };

        var result = await provider.CollectAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Readings);
        Assert.Equal(expectedError, result.ErrorCode);
    }

    [Fact]
    public async Task MockProvider_DependencyInjectionReturnsCompleteSyntheticReading()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IScannerHealthProvider, MockScannerHealthProvider>();
        using var serviceProvider = services.BuildServiceProvider();

        var result = await serviceProvider.GetRequiredService<IScannerHealthProvider>()
            .CollectAsync(CancellationToken.None);

        var reading = Assert.Single(result.Readings);
        Assert.Equal(ScannerProtocol.Mock, reading.Protocol);
        Assert.Contains("Mock", reading.Manufacturer, StringComparison.Ordinal);
        Assert.NotNull(reading.LifetimePages);
        Assert.NotNull(reading.RollerLifePercent);
        Assert.Equal(ScannerOnlineStatus.Online, reading.OnlineStatus);
    }

    private static ScannerSourceCatalogResult Result(
        bool available,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            available,
            available
                ?
                [
                    new ScannerSourceMetadata(
                        "source-1",
                        "Acme",
                        "ScanPro",
                        "SERIAL-1",
                        "1.2.3",
                        "USB",
                        true,
                        true,
                        true,
                        ["duplex"],
                        new ScannerDriver("Driver", "2.0", "Acme"),
                        ScannerOnlineStatus.Online,
                        metadata)
                ]
                : Array.Empty<ScannerSourceMetadata>());

    private sealed class WiaCatalog : IWiaScannerSourceCatalog
    {
        private readonly ScannerSourceCatalogResult _result;
        public WiaCatalog(ScannerSourceCatalogResult result) => _result = result;
        public Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken) => Task.FromResult(_result);
    }

    private sealed class TwainCatalog : ITwainScannerSourceCatalog
    {
        private readonly ScannerSourceCatalogResult _result;
        public TwainCatalog(ScannerSourceCatalogResult result) => _result = result;
        public Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken) => Task.FromResult(_result);
    }

    private sealed class IsisCatalog : IIsisScannerSourceCatalog
    {
        private readonly ScannerSourceCatalogResult _result;
        public IsisCatalog(ScannerSourceCatalogResult result) => _result = result;
        public Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken) => Task.FromResult(_result);
    }
}
