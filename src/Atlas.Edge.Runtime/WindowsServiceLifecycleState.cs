namespace Atlas.Edge.Runtime;

public enum WindowsServiceLifecyclePhase
{
    Startup,
    Running,
    Stopping,
    Stopped
}

public sealed record WindowsServiceLifecycleSnapshot(
    WindowsServiceLifecyclePhase Phase,
    DateTimeOffset ProcessStartedAtUtc,
    DateTimeOffset LastTransitionAtUtc,
    DateTimeOffset? RunningSinceUtc,
    DateTimeOffset? LastHealthHeartbeatUtc,
    long StartupCount,
    long RestartCount);

public sealed class WindowsServiceLifecycleState
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private WindowsServiceLifecycleSnapshot _current;

    public WindowsServiceLifecycleState(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        var now = timeProvider.GetUtcNow();
        _current = new WindowsServiceLifecycleSnapshot(
            WindowsServiceLifecyclePhase.Startup,
            now,
            now,
            null,
            null,
            0,
            0);
    }

    public WindowsServiceLifecycleSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public void RecordStartup()
    {
        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            var startupCount = _current.StartupCount + 1;
            _current = _current with
            {
                Phase = WindowsServiceLifecyclePhase.Startup,
                LastTransitionAtUtc = now,
                RunningSinceUtc = null,
                LastHealthHeartbeatUtc = null,
                StartupCount = startupCount,
                RestartCount = Math.Max(0, startupCount - 1)
            };
        }
    }

    public void RecordRunning()
    {
        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            _current = _current with
            {
                Phase = WindowsServiceLifecyclePhase.Running,
                LastTransitionAtUtc = now,
                RunningSinceUtc = now
            };
        }
    }

    public void RecordStopping() => TransitionTo(WindowsServiceLifecyclePhase.Stopping);

    public void RecordStopped() => TransitionTo(WindowsServiceLifecyclePhase.Stopped);

    public void RecordHealthHeartbeat()
    {
        lock (_sync)
        {
            _current = _current with { LastHealthHeartbeatUtc = _timeProvider.GetUtcNow() };
        }
    }

    private void TransitionTo(WindowsServiceLifecyclePhase phase)
    {
        lock (_sync)
        {
            _current = _current with
            {
                Phase = phase,
                LastTransitionAtUtc = _timeProvider.GetUtcNow()
            };
        }
    }
}
