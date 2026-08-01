using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Atlas.Edge.ScannerDiscovery;
using Microsoft.Extensions.Logging;

namespace Atlas.Edge.ScannerHealth;

public sealed class ScannerHealthService : IScannerHealthService
{
    private static readonly ScannerHealthScore UnknownScore = new(null, null, null, null, null);
    private readonly IReadOnlyList<IScannerHealthProvider> _providers;
    private readonly HealthScoreCalculator _scoreCalculator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScannerHealthService> _logger;

    public ScannerHealthService(
        IEnumerable<IScannerHealthProvider> providers,
        HealthScoreCalculator scoreCalculator,
        TimeProvider timeProvider,
        ILogger<ScannerHealthService> logger)
    {
        _providers = providers.ToArray();
        _scoreCalculator = scoreCalculator;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ScannerHealthCollectionSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var readings = new List<ScannerHealthReading>();
        var diagnostics = ImmutableArray.CreateBuilder<ScannerHealthProviderDiagnostic>();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScannerHealthProviderResult result;
            try
            {
                result = await provider.CollectAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                result = ScannerHealthProviderResult.Failed(provider.Protocol, "provider_failure");
                _logger.LogWarning(
                    "Scanner health provider {Protocol} failed; other providers will continue.",
                    provider.Protocol);
            }

            readings.AddRange(result.Readings);
            diagnostics.Add(new ScannerHealthProviderDiagnostic(
                result.Protocol,
                result.IsAvailable,
                result.Readings.Length,
                result.ErrorCode));
        }

        var capturedAtUtc = _timeProvider.GetUtcNow();
        var snapshots = readings
            .GroupBy(CreateMergeKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateSnapshot(group.Key, group, capturedAtUtc))
            .OrderBy(snapshot => snapshot.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.Model, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        return new ScannerHealthCollectionSnapshot(capturedAtUtc, snapshots, diagnostics.ToImmutable());
    }

    private ScannerHealthSnapshot CreateSnapshot(
        string mergeKey,
        IEnumerable<ScannerHealthReading> sourceReadings,
        DateTimeOffset capturedAtUtc)
    {
        var readings = sourceReadings.OrderBy(reading => Priority(reading.Protocol)).ToArray();
        var consumablesKnown = readings.Any(reading => reading.ConsumablesKnown);
        var countersKnown = readings.Any(reading => reading.MaintenanceCountersKnown);
        var snapshot = new ScannerHealthSnapshot(
            CreateScannerId(mergeKey),
            capturedAtUtc,
            FirstText(readings.Select(reading => reading.Manufacturer)) ?? "Unknown",
            FirstText(readings.Select(reading => reading.Model)) ?? "Unknown",
            FirstText(readings.Select(reading => reading.SerialNumber)),
            FirstValue(readings.Select(reading => reading.LifetimePages)),
            FirstValue(readings.Select(reading => reading.DailyPages)),
            FirstValue(readings.Select(reading => reading.RollerLifePercent)),
            FirstValue(readings.Select(reading => reading.PadLifePercent)),
            MergeConsumables(readings),
            consumablesKnown,
            MergeCounters(readings),
            countersKnown,
            FirstText(readings.Select(reading => reading.FirmwareVersion)),
            FirstValue(readings.Select(reading => reading.ScanSpeedPagesPerMinute)),
            FirstValue(readings.Select(reading => reading.RatedScanSpeedPagesPerMinute)),
            FirstValue(readings.Select(reading => reading.JamCount)),
            FirstValue(readings.Select(reading => reading.DoubleFeedCount)),
            FirstValue(readings.Select(reading => reading.TransportErrorCount)),
            MergeOnlineStatus(readings.Select(reading => reading.OnlineStatus)),
            MergeDriverStatus(readings.Select(reading => reading.DriverStatus)),
            readings.Select(reading => reading.UsbStability).FirstOrDefault(value => value is not null),
            FirstValue(readings.Select(reading => reading.DeviceUptime)),
            readings.Select(reading => reading.Protocol).Distinct().OrderBy(protocol => protocol).ToImmutableArray(),
            UnknownScore);

        return snapshot with { Score = _scoreCalculator.Calculate(snapshot) };
    }

    private static ImmutableArray<ScannerConsumableHealth> MergeConsumables(
        IEnumerable<ScannerHealthReading> readings) =>
        readings
            .SelectMany(reading => reading.Consumables)
            .GroupBy(consumable => consumable.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(consumable => consumable.Name, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

    private static ImmutableDictionary<string, long> MergeCounters(
        IEnumerable<ScannerHealthReading> readings) =>
        readings
            .SelectMany(reading => reading.MaintenanceCounters)
            .GroupBy(counter => counter.Key, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);

    private static ScannerOnlineStatus MergeOnlineStatus(IEnumerable<ScannerOnlineStatus> values)
    {
        var statuses = values.ToArray();
        if (statuses.Contains(ScannerOnlineStatus.Online))
        {
            return ScannerOnlineStatus.Online;
        }

        return statuses.Any(status => status == ScannerOnlineStatus.Offline)
            ? ScannerOnlineStatus.Offline
            : ScannerOnlineStatus.Unknown;
    }

    private static ScannerDriverHealthStatus MergeDriverStatus(IEnumerable<ScannerDriverHealthStatus> values)
    {
        var statuses = values.ToArray();
        if (statuses.Contains(ScannerDriverHealthStatus.Error))
        {
            return ScannerDriverHealthStatus.Error;
        }

        if (statuses.Contains(ScannerDriverHealthStatus.Degraded))
        {
            return ScannerDriverHealthStatus.Degraded;
        }

        return statuses.Contains(ScannerDriverHealthStatus.Ready)
            ? ScannerDriverHealthStatus.Ready
            : ScannerDriverHealthStatus.Unknown;
    }

    private static string CreateMergeKey(ScannerHealthReading reading) =>
        !string.IsNullOrWhiteSpace(reading.SerialNumber)
            ? $"serial|{Normalize(reading.Manufacturer)}|{Normalize(reading.SerialNumber)}"
            : $"source|{Normalize(reading.Manufacturer)}|{Normalize(reading.Model)}|{Normalize(reading.SourceId)}";

    private static string CreateScannerId(string mergeKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(mergeKey));
        return $"scanner-{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}";
    }

    private static string? FirstText(IEnumerable<string?> values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static T? FirstValue<T>(IEnumerable<T?> values) where T : struct =>
        values.FirstOrDefault(value => value.HasValue);

    private static int Priority(ScannerProtocol protocol) => protocol switch
    {
        ScannerProtocol.Wia => 0,
        ScannerProtocol.Twain => 1,
        ScannerProtocol.Isis => 2,
        ScannerProtocol.Mock => 3,
        _ => int.MaxValue
    };

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToUpperInvariant();
}
