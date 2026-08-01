using Atlas.Edge.ScannerConnectors;

namespace Atlas.Edge.Runtime;

public sealed class ScannerConnectorHostedService : IHostedService
{
    private readonly IScannerConnectorManager _connectorManager;
    private readonly ScannerConnectorState _connectorState;
    private readonly ILogger<ScannerConnectorHostedService> _logger;

    public ScannerConnectorHostedService(
        IScannerConnectorManager connectorManager,
        ScannerConnectorState connectorState,
        ILogger<ScannerConnectorHostedService> logger)
    {
        _connectorManager = connectorManager;
        _connectorState = connectorState;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _connectorManager.CollectAsync(cancellationToken);
            _connectorState.Update(snapshot);

            _logger.LogInformation(
                "Scanner connector collection completed with {ScannerCount} normalized snapshots.",
                snapshot.Scanners.Length);

            foreach (var diagnostic in snapshot.Diagnostics)
            {
                _logger.LogInformation(
                    "Scanner connector {ConnectorId} operation {Operation}: state {State}, status {StatusCode}.",
                    diagnostic.ConnectorId,
                    diagnostic.Operation,
                    diagnostic.State,
                    diagnostic.ErrorCode ?? "ok");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Scanner connector collection was canceled during runtime startup.");
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Scanner connector collection failed during runtime startup; the runtime will continue without a connector snapshot.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
