using System.Collections.Immutable;

namespace Atlas.Edge.Runtime;

public enum MissionControlSubsystemState
{
    Available,
    Empty,
    Unavailable,
    PartialFailure
}

public enum MissionControlHealthBand
{
    Healthy,
    Warning,
    Critical,
    Unknown
}

public sealed record MissionControlTenantScope(
    string TenantId,
    string TenantName,
    string? SiteName = null);

public sealed record MissionControlSubsystem(
    string Name,
    MissionControlSubsystemState State,
    string DisplayMessage);

public sealed record MissionControlFleetSummary(
    int? FleetConfidence,
    int TotalScanners,
    int OnlineScanners,
    int OfflineScanners,
    int UnknownOnlineState,
    int HealthyScanners,
    int WarningScanners,
    int CriticalScanners,
    int UnknownHealth,
    int EvidenceObservations,
    int PatternsIdentified,
    int UnknownPatterns);

public sealed record MissionControlEvidenceField(
    string Category,
    string Name,
    string State,
    string? DisplayValue,
    string? ErrorCode = null);

public sealed record MissionControlPattern(
    string PatternId,
    string MatchLevel,
    int SimilarityScore,
    ImmutableArray<string> MatchedFields,
    ImmutableArray<string> DifferentFields,
    long OccurrenceCount,
    DateTimeOffset FirstObservedUtc,
    DateTimeOffset LastObservedUtc);

public sealed record MissionControlScanner(
    string ScannerId,
    string Manufacturer,
    string Model,
    string TenantName,
    string? SiteName,
    string OnlineState,
    int? HealthScore,
    MissionControlHealthBand HealthBand,
    ImmutableArray<string> ConnectorSources,
    int EvidenceCount,
    MissionControlPattern? Pattern,
    DateTimeOffset? LastObservedUtc,
    ImmutableArray<MissionControlEvidenceField> Evidence,
    ImmutableArray<string> Provenance);

public sealed record MissionControlView(
    string TenantId,
    string TenantName,
    DateTimeOffset GeneratedAtUtc,
    MissionControlFleetSummary Summary,
    ImmutableArray<MissionControlScanner> Scanners,
    ImmutableArray<MissionControlSubsystem> Subsystems);
