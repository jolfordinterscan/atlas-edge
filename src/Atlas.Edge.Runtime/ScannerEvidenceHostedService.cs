using Atlas.Edge.ScannerEvidence;

namespace Atlas.Edge.Runtime;

public sealed class ScannerEvidenceHostedService : IHostedService
{
    private readonly IScannerEvidenceManager _manager;
    private readonly ScannerEvidenceState _state;
    private readonly ILogger<ScannerEvidenceHostedService> _logger;

    public ScannerEvidenceHostedService(
        IScannerEvidenceManager manager,
        ScannerEvidenceState state,
        ILogger<ScannerEvidenceHostedService> logger)
    {
        _manager = manager;
        _state = state;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _manager.CollectAsync(cancellationToken);
            _state.Update(snapshot);
            _logger.LogInformation(
                "Scanner evidence collection completed with {ScannerCount} immutable snapshots.",
                snapshot.Scanners.Length);

            foreach (var diagnostic in snapshot.Diagnostics)
            {
                _logger.LogInformation(
                    "Scanner evidence provider {ProviderId} operation {Operation}: state {State}, status {StatusCode}.",
                    diagnostic.ProviderId,
                    diagnostic.Operation,
                    diagnostic.State,
                    diagnostic.ErrorCode ?? "ok");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Scanner evidence collection was canceled during runtime startup.");
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Scanner evidence collection failed during runtime startup; heartbeat operation will continue.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
