using System.Collections.Immutable;
using System.Globalization;
using Atlas.Edge.Patterns;
using Atlas.Edge.ScannerConnectors;
using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.ScannerEvidence;
using Atlas.Edge.ScannerHealth;

namespace Atlas.Edge.Runtime;

public sealed class MissionControlApplicationService
{
    private readonly ScannerConnectorState _connectorState;
    private readonly ScannerEvidenceState _evidenceState;
    private readonly ScannerHealthState _healthState;
    private readonly ScannerInventoryState _inventoryState;
    private readonly object _patternLock = new();
    private readonly Dictionary<PatternFingerprint, ObservedPattern> _patternHistory = new();
    private readonly PatternEngine _patterns;
    private readonly TimeProvider _timeProvider;

    public MissionControlApplicationService(
        ScannerInventoryState inventoryState,
        ScannerHealthState healthState,
        ScannerConnectorState connectorState,
        ScannerEvidenceState evidenceState,
        PatternEngine patterns,
        TimeProvider timeProvider)
    {
        _inventoryState = inventoryState;
        _healthState = healthState;
        _connectorState = connectorState;
        _evidenceState = evidenceState;
        _patterns = patterns;
        _timeProvider = timeProvider;
    }

    public MissionControlView Read(MissionControlTenantScope tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (string.IsNullOrWhiteSpace(tenant.TenantId) || string.IsNullOrWhiteSpace(tenant.TenantName))
        {
            throw new ArgumentException("Mission Control requires an explicit tenant scope.", nameof(tenant));
        }

        var inventory = _inventoryState.Current;
        var health = _healthState.Current;
        var connectors = _connectorState.Current;
        var evidence = _evidenceState.Current;
        var scanners = inventory?.Scanners.Select(scanner => BuildScanner(
                tenant,
                scanner,
                inventory.DiscoveredAtUtc,
                health?.Scanners.FirstOrDefault(item => item.ScannerId == scanner.DiscoveryId),
                connectors?.Scanners.FirstOrDefault(item => item.ScannerId == scanner.DiscoveryId),
                FindEvidence(scanner, evidence),
                evidence?.CollectedAtUtc))
            .ToImmutableArray() ?? ImmutableArray<MissionControlScanner>.Empty;

        return new MissionControlView(
            tenant.TenantId.Trim(),
            tenant.TenantName.Trim(),
            _timeProvider.GetUtcNow(),
            Summarize(scanners),
            scanners,
            [
                Subsystem("Discovery", inventory is not null, inventory?.Scanners.Count ?? 0,
                    inventory?.Diagnostics.Any(item => !item.IsAvailable || item.ErrorCode is not null) == true),
                Subsystem("Health", health is not null, health?.Scanners.Length ?? 0,
                    health?.Diagnostics.Any(item => !item.IsAvailable || item.ErrorCode is not null) == true),
                Subsystem("Connectors", connectors is not null, connectors?.Scanners.Length ?? 0,
                    connectors?.Diagnostics.Any(item =>
                        item.State is ConnectorResultState.Failed or ConnectorResultState.Unavailable) == true),
                Subsystem("Evidence", evidence is not null, evidence?.Scanners.Length ?? 0,
                    evidence?.Diagnostics.Any(item =>
                        item.State is EvidenceValueState.Failed or EvidenceValueState.Unavailable) == true),
                Subsystem("Patterns", evidence is not null, scanners.Count(item => item.Pattern is not null), false)
            ]);
    }

    private MissionControlScanner BuildScanner(
        MissionControlTenantScope tenant,
        DiscoveredScanner scanner,
        DateTimeOffset discoveredAtUtc,
        ScannerHealthSnapshot? health,
        ScannerConnectorSnapshot? connector,
        ScannerEvidenceSnapshot? evidence,
        DateTimeOffset? evidenceCollectedAtUtc)
    {
        var connectorSources = connector?.Provenance.Select(item => item.Protocol)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray() ?? scanner.Protocols.Select(item => item.ToString()).ToImmutableArray();
        return new MissionControlScanner(
            scanner.DiscoveryId,
            TextOrUnknown(scanner.Manufacturer),
            TextOrUnknown(scanner.Model),
            tenant.TenantName.Trim(),
            string.IsNullOrWhiteSpace(tenant.SiteName) ? null : tenant.SiteName.Trim(),
            scanner.OnlineStatus.ToString(),
            health?.Score.Overall,
            Band(health?.Score.Overall),
            connectorSources,
            evidence?.Observations.Length ?? 0,
            evidence is null ? null : ObservePattern(evidence, evidenceCollectedAtUtc),
            Latest(discoveredAtUtc, health?.CapturedAtUtc, evidenceCollectedAtUtc),
            evidence is null ? ImmutableArray<MissionControlEvidenceField>.Empty : FlattenEvidence(evidence),
            evidence?.Provenance.Select(item => $"{item.SourceType} · {item.SourceQuality}")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToImmutableArray() ?? ImmutableArray<string>.Empty);
    }

    private MissionControlPattern? ObservePattern(
        ScannerEvidenceSnapshot evidence,
        DateTimeOffset? sourceObservedAtUtc)
    {
        PatternFingerprint fingerprint;
        try
        {
            fingerprint = _patterns.Fingerprint(evidence);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var sourceObservedAt = sourceObservedAtUtc ?? _timeProvider.GetUtcNow();
        lock (_patternLock)
        {
            if (!_patternHistory.TryGetValue(fingerprint, out var observed) ||
                observed.LastSourceObservationUtc != sourceObservedAt)
            {
                observed = new ObservedPattern(_patterns.CreatePattern(evidence).History, sourceObservedAt);
                _patternHistory[fingerprint] = observed;
            }

            return new MissionControlPattern(
                fingerprint.PatternId.Value,
                PatternMatchLevel.ExactMatch.ToString(),
                100,
                fingerprint.Summary.Fields.Select(item => item.Name).ToImmutableArray(),
                ImmutableArray<string>.Empty,
                observed.History.OccurrenceCount,
                observed.History.FirstObservedUtc,
                observed.History.LastObservedUtc);
        }
    }

    private static ScannerEvidenceSnapshot? FindEvidence(
        DiscoveredScanner scanner,
        ScannerEvidenceCollectionSnapshot? evidence)
    {
        if (evidence is null || string.IsNullOrWhiteSpace(scanner.SerialNumber))
        {
            return null;
        }

        return evidence.Scanners.FirstOrDefault(candidate =>
            candidate.Identity.State == EvidenceValueState.Known &&
            candidate.Identity.Value.Manufacturer.State == EvidenceValueState.Known &&
            candidate.Identity.Value.SerialNumber.State == EvidenceValueState.Known &&
            string.Equals(
                candidate.Identity.Value.Manufacturer.Value,
                scanner.Manufacturer,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                candidate.Identity.Value.SerialNumber.Value,
                scanner.SerialNumber,
                StringComparison.OrdinalIgnoreCase));
    }

    private static MissionControlFleetSummary Summarize(ImmutableArray<MissionControlScanner> scanners)
    {
        var knownScores = scanners.Where(item => item.HealthScore.HasValue)
            .Select(item => item.HealthScore!.Value)
            .ToArray();
        var patternCount = scanners.Where(item => item.Pattern is not null)
            .Select(item => item.Pattern!.PatternId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        return new MissionControlFleetSummary(
            knownScores.Length == 0
                ? null
                : (int)Math.Round(knownScores.Average(), MidpointRounding.AwayFromZero),
            scanners.Length,
            scanners.Count(item => item.OnlineState == nameof(ScannerOnlineStatus.Online)),
            scanners.Count(item => item.OnlineState == nameof(ScannerOnlineStatus.Offline)),
            scanners.Count(item => item.OnlineState == nameof(ScannerOnlineStatus.Unknown)),
            scanners.Count(item => item.HealthBand == MissionControlHealthBand.Healthy),
            scanners.Count(item => item.HealthBand == MissionControlHealthBand.Warning),
            scanners.Count(item => item.HealthBand == MissionControlHealthBand.Critical),
            scanners.Count(item => item.HealthBand == MissionControlHealthBand.Unknown),
            scanners.Sum(item => item.EvidenceCount),
            patternCount,
            scanners.Count(item => item.Pattern is null));
    }

    private static ImmutableArray<MissionControlEvidenceField> FlattenEvidence(ScannerEvidenceSnapshot evidence)
    {
        var fields = ImmutableArray.CreateBuilder<MissionControlEvidenceField>();
        AddNested(fields, "Identity", "Manufacturer", evidence.Identity, item => item.Manufacturer);
        AddNested(fields, "Identity", "Model", evidence.Identity, item => item.Model);
        AddNested(fields, "Driver", "Package", evidence.Driver, item => item.PackageName);
        AddNested(fields, "Driver", "Version", evidence.Driver, item => item.Version);
        AddNested(fields, "Driver", "Provider", evidence.Driver, item => item.Provider);
        AddNested(fields, "Firmware", "Version", evidence.Firmware, item => item.Version);
        AddNested(fields, "Connection", "Present", evidence.Connection, item => item.Present);
        AddDictionary(fields, "Counters", evidence.Counters, item => item.Counters);
        AddDictionary(fields, "Maintenance", evidence.Maintenance, item => item.Values);
        AddServices(fields, evidence.Services);
        AddEvents(fields, evidence.Events);
        AddLogs(fields, evidence.LogReferences);
        AddNested(fields, "Network", "Present", evidence.Network, item => item.Present);
        AddNested(fields, "Network", "Firmware", evidence.Network, item => item.Firmware);
        AddNested(fields, "Network", "Error state", evidence.Network, item => item.ErrorState);
        if (evidence.Network.State == EvidenceValueState.Known)
        {
            AddNested(fields, "Network", "Uptime", evidence.Network, item => item.Uptime);
            AddNested(fields, "Network", "Counters", evidence.Network, item => item.Counters,
                value => string.Join(", ", value.Counters.Where(item => item.Value.State == EvidenceValueState.Known)
                    .Select(item => $"{item.Key}: {item.Value.Value.ToString(CultureInfo.InvariantCulture)}")));
        }

        return fields.ToImmutable();
    }

    private static void AddServices(
        ImmutableArray<MissionControlEvidenceField>.Builder fields,
        EvidenceValue<ImmutableArray<ServiceEvidence>> services)
    {
        if (services.State != EvidenceValueState.Known)
        {
            AddState(fields, "Services", "Availability", services.State, services.ErrorCode);
            return;
        }

        foreach (var service in services.Value)
        {
            AddValue(fields, "Services", service.ServiceName, service.State);
        }
    }

    private static void AddEvents(
        ImmutableArray<MissionControlEvidenceField>.Builder fields,
        EvidenceValue<ImmutableArray<EventEvidence>> events)
    {
        if (events.State != EvidenceValueState.Known)
        {
            AddState(fields, "Events", "Availability", events.State, events.ErrorCode);
            return;
        }

        foreach (var item in events.Value)
        {
            fields.Add(new MissionControlEvidenceField(
                "Events",
                item.StableEventCode,
                EvidenceValueState.Known.ToString(),
                item.Kind.ToString()));
        }
    }

    private static void AddLogs(
        ImmutableArray<MissionControlEvidenceField>.Builder fields,
        EvidenceValue<ImmutableArray<LogEvidenceReference>> logs)
    {
        if (logs.State != EvidenceValueState.Known)
        {
            AddState(fields, "Log references", "Availability", logs.State, logs.ErrorCode);
            return;
        }

        var codes = logs.Value.SelectMany(item => item.StableErrorCodes).Distinct(StringComparer.Ordinal).ToArray();
        if (codes.Length == 0)
        {
            fields.Add(new MissionControlEvidenceField(
                "Log references",
                "Stable error codes",
                EvidenceValueState.Unknown.ToString(),
                null,
                EvidenceErrorCodes.DataUnknown));
            return;
        }

        foreach (var code in codes)
        {
            fields.Add(new MissionControlEvidenceField(
                "Log references",
                "Stable error code",
                EvidenceValueState.Known.ToString(),
                code));
        }
    }

    private static void AddDictionary<TOuter, TValue>(
        ImmutableArray<MissionControlEvidenceField>.Builder fields,
        string category,
        EvidenceValue<TOuter> outer,
        Func<TOuter, ImmutableDictionary<string, EvidenceValue<TValue>>> selector)
    {
        if (outer.State != EvidenceValueState.Known)
        {
            AddState(fields, category, "Availability", outer.State, outer.ErrorCode);
            return;
        }

        foreach (var item in selector(outer.Value).OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddValue(fields, category, item.Key, item.Value);
        }
    }

    private static void AddNested<TOuter, TValue>(
        ImmutableArray<MissionControlEvidenceField>.Builder fields,
        string category,
        string name,
        EvidenceValue<TOuter> outer,
        Func<TOuter, EvidenceValue<TValue>> selector,
        Func<TValue, string>? formatter = null)
    {
        if (outer.State != EvidenceValueState.Known)
        {
            AddState(fields, category, name, outer.State, outer.ErrorCode);
            return;
        }

        AddValue(fields, category, name, selector(outer.Value), formatter);
    }

    private static void AddValue<T>(
        ImmutableArray<MissionControlEvidenceField>.Builder fields,
        string category,
        string name,
        EvidenceValue<T> value,
        Func<T, string>? formatter = null) =>
        fields.Add(new MissionControlEvidenceField(
            category,
            name,
            value.State.ToString(),
            value.State == EvidenceValueState.Known
                ? formatter?.Invoke(value.Value) ?? Convert.ToString(value.Value, CultureInfo.InvariantCulture)
                : null,
            value.ErrorCode));

    private static void AddState(
        ImmutableArray<MissionControlEvidenceField>.Builder fields,
        string category,
        string name,
        EvidenceValueState state,
        string? errorCode) =>
        fields.Add(new MissionControlEvidenceField(category, name, state.ToString(), null, errorCode));

    private static MissionControlHealthBand Band(int? score) => score switch
    {
        >= 85 => MissionControlHealthBand.Healthy,
        >= 60 => MissionControlHealthBand.Warning,
        >= 0 => MissionControlHealthBand.Critical,
        _ => MissionControlHealthBand.Unknown
    };

    private static MissionControlSubsystem Subsystem(string name, bool exists, int count, bool partialFailure) =>
        !exists
            ? new MissionControlSubsystem(name, MissionControlSubsystemState.Unavailable, "Not yet available")
            : partialFailure
                ? new MissionControlSubsystem(name, MissionControlSubsystemState.PartialFailure, "Some sources are unavailable")
                : count == 0
                    ? new MissionControlSubsystem(name, MissionControlSubsystemState.Empty, "No data collected")
                    : new MissionControlSubsystem(name, MissionControlSubsystemState.Available, "Available");

    private static DateTimeOffset? Latest(params DateTimeOffset?[] values)
    {
        var known = values.Where(item => item.HasValue).Select(item => item!.Value).ToArray();
        return known.Length == 0 ? null : known.Max();
    }

    private static string TextOrUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();

    private sealed record ObservedPattern(PatternHistory History, DateTimeOffset LastSourceObservationUtc);
}
