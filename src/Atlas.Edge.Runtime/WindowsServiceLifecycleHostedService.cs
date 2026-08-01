namespace Atlas.Edge.Runtime;

public sealed class WindowsServiceLifecycleHostedService : IHostedLifecycleService
{
    public static readonly EventId StartingEvent = new(1500, "WindowsServiceStarting");
    public static readonly EventId RunningEvent = new(1501, "WindowsServiceRunning");
    public static readonly EventId StoppingEvent = new(1502, "WindowsServiceStopping");
    public static readonly EventId StoppedEvent = new(1503, "WindowsServiceStopped");

    private readonly WindowsServiceLifecycleState _state;
    private readonly ILogger<WindowsServiceLifecycleHostedService> _logger;

    public WindowsServiceLifecycleHostedService(
        WindowsServiceLifecycleState state,
        ILogger<WindowsServiceLifecycleHostedService> logger)
    {
        _state = state;
        _logger = logger;
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _state.RecordStartup();
        _logger.LogInformation(StartingEvent, "Atlas Edge Windows Service is starting.");
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        _state.RecordRunning();
        _logger.LogInformation(RunningEvent, "Atlas Edge Windows Service is running.");
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        _state.RecordStopping();
        _logger.LogInformation(StoppingEvent, "Atlas Edge Windows Service is stopping.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        _state.RecordStopped();
        _logger.LogInformation(StoppedEvent, "Atlas Edge Windows Service stopped gracefully.");
        return Task.CompletedTask;
    }
}
