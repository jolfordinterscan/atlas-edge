namespace Atlas.Edge.ScannerHealth;

public sealed class HealthScoreCalculator
{
    public ScannerHealthScore Calculate(ScannerHealthSnapshot snapshot)
    {
        var mechanical = CalculateMechanical(snapshot);
        var reliability = CalculateReliability(snapshot);
        var performance = CalculatePerformance(snapshot);
        var connectivity = CalculateConnectivity(snapshot);
        var knownCategories = new[] { mechanical, reliability, performance, connectivity }
            .Where(score => score.HasValue)
            .Select(score => score!.Value)
            .ToArray();

        return new ScannerHealthScore(
            mechanical,
            reliability,
            performance,
            connectivity,
            knownCategories.Length == 0 ? null : Round((decimal)knownCategories.Average()));
    }

    private static int? CalculateMechanical(ScannerHealthSnapshot snapshot)
    {
        var known = new List<decimal>();
        AddPercent(known, snapshot.RollerLifePercent);
        AddPercent(known, snapshot.PadLifePercent);
        foreach (var consumable in snapshot.Consumables)
        {
            AddPercent(known, consumable.RemainingPercent);
        }

        return known.Count == 0 ? null : Round(known.Average());
    }

    private static int? CalculateReliability(ScannerHealthSnapshot snapshot)
    {
        if (!snapshot.LifetimePages.HasValue || snapshot.LifetimePages.Value <= 0)
        {
            return null;
        }

        if (!snapshot.JamCount.HasValue &&
            !snapshot.DoubleFeedCount.HasValue &&
            !snapshot.TransportErrorCount.HasValue)
        {
            return null;
        }

        var weightedIncidents =
            (snapshot.JamCount ?? 0) +
            (snapshot.DoubleFeedCount ?? 0) * 2m +
            (snapshot.TransportErrorCount ?? 0) * 3m;
        var weightedIncidentsPerThousandPages = weightedIncidents * 1000m / snapshot.LifetimePages.Value;
        return Round(100m - Math.Min(100m, weightedIncidentsPerThousandPages * 10m));
    }

    private static int? CalculatePerformance(ScannerHealthSnapshot snapshot)
    {
        if (!snapshot.ScanSpeedPagesPerMinute.HasValue ||
            !snapshot.RatedScanSpeedPagesPerMinute.HasValue ||
            snapshot.RatedScanSpeedPagesPerMinute.Value <= 0)
        {
            return null;
        }

        return Round(Math.Min(
            100m,
            snapshot.ScanSpeedPagesPerMinute.Value / snapshot.RatedScanSpeedPagesPerMinute.Value * 100m));
    }

    private static int? CalculateConnectivity(ScannerHealthSnapshot snapshot)
    {
        var known = new List<decimal>();
        if (snapshot.OnlineStatus != Atlas.Edge.ScannerDiscovery.ScannerOnlineStatus.Unknown)
        {
            known.Add(snapshot.OnlineStatus == Atlas.Edge.ScannerDiscovery.ScannerOnlineStatus.Online ? 100m : 0m);
        }

        if (snapshot.DriverStatus != ScannerDriverHealthStatus.Unknown)
        {
            known.Add(snapshot.DriverStatus switch
            {
                ScannerDriverHealthStatus.Ready => 100m,
                ScannerDriverHealthStatus.Degraded => 50m,
                ScannerDriverHealthStatus.Error => 0m,
                _ => throw new InvalidOperationException("Unexpected driver status.")
            });
        }

        if (snapshot.UsbStability?.DisconnectCount is >= 0 && snapshot.DeviceUptime is { TotalDays: > 0 })
        {
            var disconnectsPerDay = snapshot.UsbStability.DisconnectCount.Value / (decimal)snapshot.DeviceUptime.Value.TotalDays;
            known.Add(Math.Max(0m, 100m - (disconnectsPerDay * 10m)));
        }

        return known.Count == 0 ? null : Round(known.Average());
    }

    private static void AddPercent(ICollection<decimal> values, decimal? value)
    {
        if (value is >= 0 and <= 100)
        {
            values.Add(value.Value);
        }
    }

    private static int Round(decimal value) =>
        Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);
}
