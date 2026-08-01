using Atlas.Edge.Configuration;
using Atlas.Edge.Core;
using Atlas.Edge.Telemetry;

namespace Atlas.Edge.Tests;

public sealed class EventIdUniquenessTests
{
    [Fact]
    public void Build_GeneratesUniqueEventIds()
    {
        var builder = new HeartbeatEventBuilder();
        var identity = new AgentIdentity("agent", "workstation", "tenant", "Development", true, DateTimeOffset.UtcNow);
        var options = new AtlasEdgeOptions();

        var first = builder.Build(identity, options, DateTimeOffset.UtcNow);
        var second = builder.Build(identity, options, DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.NotEqual(first.EventId, second.EventId);
    }
}