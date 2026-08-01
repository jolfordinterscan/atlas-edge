using Atlas.Edge.Core;

namespace Atlas.Edge.Runtime;

public sealed class RuntimeState
{
    private readonly object _sync = new();

    public RuntimeHealthState Current { get; private set; } =
        new(RuntimeStatus.Stopped, DateTimeOffset.UtcNow, "Runtime not started.", null);

    public void Update(RuntimeStatus status, string message, string? lastError = null)
    {
        lock (_sync)
        {
            Current = new RuntimeHealthState(status, DateTimeOffset.UtcNow, message, lastError);
        }
    }
}