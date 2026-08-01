using Atlas.Edge.Configuration;
using Atlas.Edge.Core;
using Atlas.Edge.Telemetry;

namespace Atlas.Edge.Tests;

public sealed class HeartbeatContractTests
{
    [Fact]
    public void Build_CreatesHeartbeatWithExpectedContract()
    {
        var builder = new HeartbeatEventBuilder();
        var now = DateTimeOffset.UtcNow;
        var identity = new AgentIdentity("agent-1", "workstation-1", "tenant-a", "Development", true, now);
        var options = new AtlasEdgeOptions();

        var heartbeat = builder.Build(identity, options, now);

        Assert.Equal("agent.heartbeat", heartbeat.EventType);
        Assert.Equal("1.0", heartbeat.SchemaVersion);
        Assert.Equal("agent-1", heartbeat.AgentId);
        Assert.Equal("workstation-1", heartbeat.WorkstationId);
        Assert.Equal("tenant-a", heartbeat.TenantBinding);
        Assert.Equal("runtime.foundation", heartbeat.SourceAdapter);
    }
}