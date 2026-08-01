using Atlas.Edge.ScannerHealth;

namespace Atlas.Edge.Runtime;

public sealed class ScannerHealthHostedService : IHostedService
{
    private readonly IScannerHealthService _healthService;
    private readonly ILogger<ScannerHealthHostedService> _logger;
    private readonly ScannerHealthState _healthState;

    public ScannerHealthHostedService(
        IScannerHealthService healthService,
        ScannerHealthState healthState,
        ILogger<ScannerHealthHostedService> logger)
    {
        _healthService = healthService;
        _healthState = healthState;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _healthService.CollectAsync(cancellationToken);
            _healthState.Update(snapshot);

            _logger.LogInformation(
                "Scanner health collection completed with {ScannerCount} normalized snapshots.",
                snapshot.Scanners.Length);

            foreach (var diagnostic in snapshot.Diagnostics)
            {
                _logger.LogInformation(
                    "Scanner health provider {Protocol}: available {IsAvailable}, scanners {ScannerCount}, status {StatusCode}.",
                    diagnostic.Protocol,
                    diagnostic.IsAvailable,
                    diagnostic.ScannerCount,
                    diagnostic.ErrorCode ?? "ok");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Scanner health collection was canceled during runtime startup.");
        }
        catch (Exception)
        {
            _logger.LogWarning("Scanner health collection failed during runtime startup; the runtime will continue without a health snapshot.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
