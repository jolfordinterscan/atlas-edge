using Atlas.Edge.Configuration;
using Atlas.Edge.Core;

namespace Atlas.Edge.Telemetry;

public sealed class HeartbeatEventBuilder
{
    public const string HeartbeatEventType = "agent.heartbeat";
    public const string SchemaVersion = "1.0";
    public const string SourceAdapter = "runtime.foundation";

    public AgentHeartbeatEvent Build(
        AgentIdentity identity,
        AtlasEdgeOptions options,
        DateTimeOffset observedTimestampUtc,
        QueueHealth? queueHealth = null)
    {
        var utcObserved = observedTimestampUtc.ToUniversalTime();

        return new AgentHeartbeatEvent(
            EventId: Guid.NewGuid().ToString("N"),
            EventType: HeartbeatEventType,
            SchemaVersion: SchemaVersion,
            EventTimestampUtc: utcObserved,
            ObservedTimestampUtc: utcObserved,
            AgentId: identity.AgentId,
            WorkstationId: identity.WorkstationId,
            TenantBinding: identity.TenantBinding,
            SourceAdapter: SourceAdapter,
            CorrelationId: null,
            EnvironmentName: options.EnvironmentName)
        {
            QueuePendingCount = queueHealth?.PendingCount,
            QueueInFlightCount = queueHealth?.InFlightCount,
            QueueStatus = queueHealth is null ? null : "Operational",
            ServiceState = "Running"
        };
    }
}
