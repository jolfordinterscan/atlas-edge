namespace Atlas.Edge.Core;

public sealed record AgentHeartbeatEvent(
    string EventId,
    string EventType,
    string SchemaVersion,
    DateTimeOffset EventTimestampUtc,
    DateTimeOffset ObservedTimestampUtc,
    string AgentId,
    string WorkstationId,
    string TenantBinding,
    string SourceAdapter,
    string? CorrelationId,
    string EnvironmentName)
{
    public int? QueuePendingCount { get; init; }

    public int? QueueInFlightCount { get; init; }

    public string? QueueStatus { get; init; }

    public string? ServiceState { get; init; }
}
