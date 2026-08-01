using Atlas.Edge.ScannerEvidence;

namespace Atlas.Edge.Runtime;

public sealed class ScannerEvidenceState
{
    private readonly object _sync = new();
    private ScannerEvidenceCollectionSnapshot? _current;

    public ScannerEvidenceCollectionSnapshot? Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public void Update(ScannerEvidenceCollectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_sync)
        {
            _current = snapshot;
        }
    }
}
