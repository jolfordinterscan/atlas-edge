using Atlas.Edge.ScannerDiscovery;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Edge.Tests;

public sealed class ScannerDiscoveryServiceTests
{
    [Fact]
    public async Task Discover_MergesSamePhysicalScannerAcrossAdapters()
    {
        var now = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
        var wiaDevice = Device(
            ScannerProtocol.Wia,
            "wia-source",
            "SERIAL-1",
            ScannerOnlineStatus.Online,
            supportsDuplex: true,
            capabilities: ["duplex"]);
        var twainDevice = Device(
            ScannerProtocol.Twain,
            "twain-source",
            "SERIAL-1",
            ScannerOnlineStatus.Unknown,
            supportsColor: true,
            capabilities: ["color"]);
        var service = CreateService(
            now,
            new StaticAdapter(ScannerProtocol.Wia, ScannerAdapterResult.Available(ScannerProtocol.Wia, [wiaDevice])),
            new StaticAdapter(ScannerProtocol.Twain, ScannerAdapterResult.Available(ScannerProtocol.Twain, [twainDevice])));

        var snapshot = await service.DiscoverAsync(CancellationToken.None);

        var scanner = Assert.Single(snapshot.Scanners);
        Assert.Equal(now, snapshot.DiscoveredAtUtc);
        Assert.Equal("Acme", scanner.Manufacturer);
        Assert.Equal("ScanPro 5000", scanner.Model);
        Assert.Equal("SERIAL-1", scanner.SerialNumber);
        Assert.True(scanner.SupportsDuplex);
        Assert.True(scanner.SupportsColor);
        Assert.Equal(ScannerOnlineStatus.Online, scanner.OnlineStatus);
        Assert.Equal([ScannerProtocol.Twain, ScannerProtocol.Wia], scanner.Protocols);
        Assert.Equal(["color", "duplex"], scanner.Capabilities);
        Assert.StartsWith("scanner-", scanner.DiscoveryId, StringComparison.Ordinal);
        Assert.DoesNotContain("SERIAL-1", scanner.DiscoveryId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discover_IsolatesAdapterFailureAndContinues()
    {
        var service = CreateService(
            DateTimeOffset.UtcNow,
            new ThrowingAdapter(ScannerProtocol.Wia),
            new StaticAdapter(
                ScannerProtocol.Twain,
                ScannerAdapterResult.Available(ScannerProtocol.Twain, [Device(ScannerProtocol.Twain)])));

        var snapshot = await service.DiscoverAsync(CancellationToken.None);

        Assert.Single(snapshot.Scanners);
        Assert.Contains(
            snapshot.Diagnostics,
            diagnostic => diagnostic.Protocol == ScannerProtocol.Wia && diagnostic.ErrorCode == "adapter_failure");
        Assert.Contains(
            snapshot.Diagnostics,
            diagnostic => diagnostic.Protocol == ScannerProtocol.Twain && diagnostic.DeviceCount == 1);
    }

    [Fact]
    public async Task Discover_PreservesUnknownCapabilityAndOnlineValues()
    {
        var service = CreateService(
            DateTimeOffset.UtcNow,
            new StaticAdapter(
                ScannerProtocol.Isis,
                ScannerAdapterResult.Available(
                    ScannerProtocol.Isis,
                    [Device(ScannerProtocol.Isis, onlineStatus: ScannerOnlineStatus.Unknown)])));

        var scanner = Assert.Single((await service.DiscoverAsync(CancellationToken.None)).Scanners);

        Assert.Null(scanner.SupportsDuplex);
        Assert.Null(scanner.SupportsColor);
        Assert.Null(scanner.HasFeeder);
        Assert.Equal(ScannerOnlineStatus.Unknown, scanner.OnlineStatus);
    }

    private static ScannerDiscoveryService CreateService(
        DateTimeOffset now,
        params IScannerDiscoveryAdapter[] adapters) =>
        new(adapters, new ManualTimeProvider(now), NullLogger<ScannerDiscoveryService>.Instance);

    private static AdapterScannerDevice Device(
        ScannerProtocol protocol,
        string sourceId = "source-1",
        string? serialNumber = null,
        ScannerOnlineStatus onlineStatus = ScannerOnlineStatus.Unknown,
        bool? supportsDuplex = null,
        bool? supportsColor = null,
        IReadOnlyList<string>? capabilities = null) =>
        new(
            sourceId,
            protocol,
            "Acme",
            "ScanPro 5000",
            serialNumber,
            "1.2.3",
            "USB",
            supportsDuplex,
            supportsColor,
            null,
            capabilities ?? Array.Empty<string>(),
            new ScannerDriver($"{protocol} Driver", "2.0", "Acme"),
            onlineStatus);

    private sealed class StaticAdapter : IScannerDiscoveryAdapter
    {
        private readonly ScannerAdapterResult _result;

        public StaticAdapter(ScannerProtocol protocol, ScannerAdapterResult result)
        {
            Protocol = protocol;
            _result = result;
        }

        public ScannerProtocol Protocol { get; }

        public Task<ScannerAdapterResult> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }

    private sealed class ThrowingAdapter : IScannerDiscoveryAdapter
    {
        public ThrowingAdapter(ScannerProtocol protocol)
        {
            Protocol = protocol;
        }

        public ScannerProtocol Protocol { get; }

        public Task<ScannerAdapterResult> DiscoverAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Raw platform detail must not escape diagnostics.");
    }
}
