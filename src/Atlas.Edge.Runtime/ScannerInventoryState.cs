using Atlas.Edge.ScannerDiscovery;

namespace Atlas.Edge.Runtime;

public sealed class ScannerInventoryState
{
    private readonly object _sync = new();
    private ScannerDiscoverySnapshot? _current;

    public ScannerDiscoverySnapshot? Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public void Update(ScannerDiscoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_sync)
        {
            _current = snapshot;
        }
    }
}
