using Atlas.Edge.ScannerConnectors;

namespace Atlas.Edge.Runtime;

public sealed class ScannerConnectorState
{
    private readonly object _sync = new();
    private ScannerConnectorCollectionSnapshot? _current;

    public ScannerConnectorCollectionSnapshot? Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public void Update(ScannerConnectorCollectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_sync)
        {
            _current = snapshot;
        }
    }
}
