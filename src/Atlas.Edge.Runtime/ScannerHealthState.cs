using Atlas.Edge.ScannerHealth;

namespace Atlas.Edge.Runtime;

public sealed class ScannerHealthState
{
    private readonly object _sync = new();
    private ScannerHealthCollectionSnapshot? _current;

    public ScannerHealthCollectionSnapshot? Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public void Update(ScannerHealthCollectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_sync)
        {
            _current = snapshot;
        }
    }
}
