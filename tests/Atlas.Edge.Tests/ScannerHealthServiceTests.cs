using System.Collections.Immutable;
using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.ScannerHealth;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Edge.Tests;

public sealed class ScannerHealthServiceTests
{
    [Fact]
    public async Task Collect_MergesSameScannerAndCalculatesScores()
    {
        var now = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
        var wia = Reading(ScannerProtocol.Wia) with
        {
            LifetimePages = 100000,
            RollerLifePercent = 80m,
            OnlineStatus = ScannerOnlineStatus.Online,
            DriverStatus = ScannerDriverHealthStatus.Ready
        };
        var twain = Reading(ScannerProtocol.Twain) with
        {
            PadLifePercent = 60m,
            ScanSpeedPagesPerMinute = 45m,
            RatedScanSpeedPagesPerMinute = 60m,
            JamCount = 1,
            DoubleFeedCount = 0,
            TransportErrorCount = 0
        };
        var service = CreateService(
            now,
            new StaticProvider(ScannerProtocol.Wia, ScannerHealthProviderResult.Available(ScannerProtocol.Wia, [wia])),
            new StaticProvider(ScannerProtocol.Twain, ScannerHealthProviderResult.Available(ScannerProtocol.Twain, [twain])));

        var collection = await service.CollectAsync(CancellationToken.None);

        var snapshot = Assert.Single(collection.Scanners);
        Assert.Equal(now, collection.CapturedAtUtc);
        Assert.Equal("SERIAL-1", snapshot.SerialNumber);
        Assert.Equal(80m, snapshot.RollerLifePercent);
        Assert.Equal(60m, snapshot.PadLifePercent);
        Assert.Collection(
            snapshot.Protocols,
            protocol => Assert.Equal(ScannerProtocol.Twain, protocol),
            protocol => Assert.Equal(ScannerProtocol.Wia, protocol));
        Assert.Equal(70, snapshot.Score.Mechanical);
        Assert.Equal(100, snapshot.Score.Reliability);
        Assert.Equal(75, snapshot.Score.Performance);
        Assert.Equal(100, snapshot.Score.Connectivity);
        Assert.Equal(86, snapshot.Score.Overall);
        Assert.DoesNotContain("SERIAL-1", snapshot.ScannerId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Collect_IsolatesProviderFailure()
    {
        var service = CreateService(
            DateTimeOffset.UtcNow,
            new ThrowingProvider(ScannerProtocol.Wia),
            new StaticProvider(
                ScannerProtocol.Twain,
                ScannerHealthProviderResult.Available(ScannerProtocol.Twain, [Reading(ScannerProtocol.Twain)])));

        var result = await service.CollectAsync(CancellationToken.None);

        Assert.Single(result.Scanners);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Protocol == ScannerProtocol.Wia && diagnostic.ErrorCode == "provider_failure");
    }

    [Fact]
    public async Task Collect_PublishesImmutableCollectionsAndUnknownFlags()
    {
        var service = CreateService(
            DateTimeOffset.UtcNow,
            new StaticProvider(
                ScannerProtocol.Wia,
                ScannerHealthProviderResult.Available(ScannerProtocol.Wia, [Reading(ScannerProtocol.Wia)])));

        var snapshot = Assert.Single((await service.CollectAsync(CancellationToken.None)).Scanners);

        Assert.False(snapshot.ConsumablesKnown);
        Assert.False(snapshot.MaintenanceCountersKnown);
        Assert.Empty(snapshot.Consumables);
        Assert.Empty(snapshot.MaintenanceCounters);
        var changed = snapshot.MaintenanceCounters.Add("new-counter", 1);
        Assert.Empty(snapshot.MaintenanceCounters);
        Assert.Single(changed);
    }

    private static ScannerHealthService CreateService(
        DateTimeOffset now,
        params IScannerHealthProvider[] providers) =>
        new(
            providers,
            new HealthScoreCalculator(),
            new ManualTimeProvider(now),
            NullLogger<ScannerHealthService>.Instance);

    private static ScannerHealthReading Reading(ScannerProtocol protocol) =>
        new(
            $"{protocol}-source",
            protocol,
            "Acme",
            "ScanPro",
            "SERIAL-1",
            null,
            null,
            null,
            null,
            ImmutableArray<ScannerConsumableHealth>.Empty,
            false,
            ImmutableDictionary<string, long>.Empty,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            ScannerOnlineStatus.Unknown,
            ScannerDriverHealthStatus.Unknown,
            null,
            null);

    private sealed class StaticProvider : IScannerHealthProvider
    {
        private readonly ScannerHealthProviderResult _result;

        public StaticProvider(ScannerProtocol protocol, ScannerHealthProviderResult result)
        {
            Protocol = protocol;
            _result = result;
        }

        public ScannerProtocol Protocol { get; }

        public Task<ScannerHealthProviderResult> CollectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }

    private sealed class ThrowingProvider : IScannerHealthProvider
    {
        public ThrowingProvider(ScannerProtocol protocol) => Protocol = protocol;

        public ScannerProtocol Protocol { get; }

        public Task<ScannerHealthProviderResult> CollectAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Raw provider error");
    }
}
