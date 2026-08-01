using Atlas.Edge.Configuration;
using Atlas.Edge.Core;
using Atlas.Edge.Telemetry;

namespace Atlas.Edge.Tests;

public sealed class UtcTimestampTests
{
    [Fact]
    public void Build_UsesUtcTimestamps()
    {
        var builder = new HeartbeatEventBuilder();
        var localTime = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.FromHours(-5));
        var identity = new AgentIdentity("agent", "workstation", "tenant", "Development", true, DateTimeOffset.UtcNow);

        var heartbeat = builder.Build(identity, new AtlasEdgeOptions(), localTime);

        Assert.Equal(TimeSpan.Zero, heartbeat.EventTimestampUtc.Offset);
        Assert.Equal(TimeSpan.Zero, heartbeat.ObservedTimestampUtc.Offset);
    }
}