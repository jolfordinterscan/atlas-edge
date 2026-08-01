using Atlas.Edge.Core;

namespace Atlas.Edge.Runtime;

public sealed class RuntimeIdentityState
{
    private AgentIdentity? _current;

    public AgentIdentity? Current => Volatile.Read(ref _current);

    public void Update(AgentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Volatile.Write(ref _current, identity);
    }
}
