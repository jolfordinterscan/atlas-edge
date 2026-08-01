using Microsoft.Extensions.Options;

namespace Atlas.Edge.Runtime;

public sealed class WindowsServiceHealthHeartbeatService : BackgroundService
{
    private readonly TimeProvider _timeProvider;
    private readonly WindowsServiceLifecycleState _state;
    private readonly TimeSpan _interval;

    public WindowsServiceHealthHeartbeatService(
        IOptions<WindowsServiceOptions> options,
        WindowsServiceLifecycleState state,
        TimeProvider timeProvider)
    {
        _state = state;
        _timeProvider = timeProvider;
        _interval = TimeSpan.FromSeconds(options.Value.HealthHeartbeatIntervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _state.RecordHealthHeartbeat();

            try
            {
                await Task.Delay(_interval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
