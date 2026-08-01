using Atlas.Edge.Configuration;
using Atlas.Edge.Core;

namespace Atlas.Edge.Runtime;

public sealed class DevelopmentIdentityProvider
{
    public AgentIdentity Create(AtlasEdgeOptions options)
    {
        return new AgentIdentity(
            options.AgentId,
            options.WorkstationId,
            options.TenantBinding,
            options.EnvironmentName,
            true,
            DateTimeOffset.UtcNow);
    }
}