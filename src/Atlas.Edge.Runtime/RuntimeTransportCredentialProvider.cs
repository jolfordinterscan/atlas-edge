using Atlas.Edge.Transport;

namespace Atlas.Edge.Runtime;

public sealed class RuntimeTransportCredentialProvider : ITransportCredentialProvider
{
    private readonly object _sync = new();
    private TransportCredentialContext? _current;

    public TransportCredentialContext? GetCurrent()
    {
        lock (_sync)
        {
            return _current;
        }
    }

    public void SetCurrent(TransportCredentialContext context)
    {
        lock (_sync)
        {
            _current = context;
        }
    }
}
