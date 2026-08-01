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
    string EnvironmentName);