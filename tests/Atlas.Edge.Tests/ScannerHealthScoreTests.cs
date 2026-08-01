using System.Collections.Immutable;
using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.ScannerHealth;

namespace Atlas.Edge.Tests;

public sealed class ScannerHealthScoreTests
{
    [Fact]
    public void Calculate_LeavesEveryScoreUnknownWithoutEvidence()
    {
        var score = new HealthScoreCalculator().Calculate(CreateSnapshot());

        Assert.Null(score.Mechanical);
        Assert.Null(score.Reliability);
        Assert.Null(score.Performance);
        Assert.Null(score.Connectivity);
        Assert.Null(score.Overall);
    }

    [Fact]
    public void Calculate_ScoresOnlyKnownNormalizedInputs()
    {
        var snapshot = CreateSnapshot() with
        {
            LifetimePages = 100000,
            RollerLifePercent = 80m,
            PadLifePercent = 60m,
            Consumables = [new ScannerConsumableHealth("feed-roller", 40m, "ready")],
            ConsumablesKnown = true,
            ScanSpeedPagesPerMinute = 45m,
            RatedScanSpeedPagesPerMinute = 60m,
            JamCount = 5,
            DoubleFeedCount = 2,
            TransportErrorCount = 1,
            OnlineStatus = ScannerOnlineStatus.Online,
            DriverStatus = ScannerDriverHealthStatus.Ready,
            UsbStability = new ScannerUsbStability(1, null),
            DeviceUptime = TimeSpan.FromDays(1)
        };

        var score = new HealthScoreCalculator().Calculate(snapshot);

        Assert.Equal(60, score.Mechanical);
        Assert.Equal(99, score.Reliability);
        Assert.Equal(75, score.Performance);
        Assert.Equal(97, score.Connectivity);
        Assert.Equal(83, score.Overall);
    }

    [Fact]
    public void Calculate_DoesNotScorePerformanceWithoutRatedSpeed()
    {
        var snapshot = CreateSnapshot() with
        {
            ScanSpeedPagesPerMinute = 45m,
            OnlineStatus = ScannerOnlineStatus.Online
        };

        var score = new HealthScoreCalculator().Calculate(snapshot);

        Assert.Null(score.Performance);
        Assert.Equal(100, score.Connectivity);
        Assert.Equal(100, score.Overall);
    }

    [Fact]
    public void Calculate_CapsMeasuredPerformanceAtOneHundred()
    {
        var snapshot = CreateSnapshot() with
        {
            ScanSpeedPagesPerMinute = 80m,
            RatedScanSpeedPagesPerMinute = 60m
        };

        Assert.Equal(100, new HealthScoreCalculator().Calculate(snapshot).Performance);
    }

    [Fact]
    public void Calculate_DoesNotScoreUsbDisconnectCountWithoutUptime()
    {
        var snapshot = CreateSnapshot() with
        {
            UsbStability = new ScannerUsbStability(50, null)
        };

        Assert.Null(new HealthScoreCalculator().Calculate(snapshot).Connectivity);
    }

    private static ScannerHealthSnapshot CreateSnapshot() =>
        new(
            "scanner-test",
            DateTimeOffset.UtcNow,
            "Acme",
            "ScanPro",
            null,
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
            null,
            ImmutableArray<ScannerProtocol>.Empty,
            new ScannerHealthScore(null, null, null, null, null));
}
