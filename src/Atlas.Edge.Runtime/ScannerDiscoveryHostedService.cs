using Atlas.Edge.Configuration;
using Atlas.Edge.Queue;
using Atlas.Edge.ScannerDiscovery;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Runtime;

public sealed class ScannerDiscoveryHostedService : BackgroundService
{
    private readonly IScannerDiscoveryService _discoveryService;
    private readonly IScannerInventoryEventBuilder _eventBuilder;
    private readonly RuntimeIdentityState _identityState;
    private readonly ScannerInventoryState _inventoryState;
    private readonly ILogger<ScannerDiscoveryHostedService> _logger;
    private readonly AtlasEdgeOptions _options;
    private readonly IEventQueue _queue;
    private readonly TimeProvider _timeProvider;

    public ScannerDiscoveryHostedService(
        IScannerDiscoveryService discoveryService,
        IScannerInventoryEventBuilder eventBuilder,
        ScannerInventoryState inventoryState,
        RuntimeIdentityState identityState,
        IEventQueue queue,
        IOptions<AtlasEdgeOptions> options,
        TimeProvider timeProvider,
        ILogger<ScannerDiscoveryHostedService> logger)
    {
        _discoveryService = discoveryService;
        _eventBuilder = eventBuilder;
        _inventoryState = inventoryState;
        _identityState = identityState;
        _queue = queue;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.ScannerDiscoveryStartupDelaySeconds > 0)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_options.ScannerDiscoveryStartupDelaySeconds),
                _timeProvider,
                stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycleAsync(stoppingToken);
            await Task.Delay(
                TimeSpan.FromSeconds(_options.ScannerDiscoveryIntervalSeconds),
                _timeProvider,
                stoppingToken);
        }
    }

    internal async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        _logger.LogInformation("Scanner discovery cycle started.");

        try
        {
            var snapshot = await _discoveryService.DiscoverAsync(cancellationToken);
            _inventoryState.Update(snapshot);

            foreach (var diagnostic in snapshot.Diagnostics)
            {
                if (!diagnostic.IsAvailable)
                {
                    _logger.LogInformation(
                        "Scanner discovery provider {Protocol} unavailable; runtime continuing.",
                        diagnostic.Protocol);
                }
                else
                {
                    _logger.LogInformation(
                        "Scanner discovery provider {Protocol} discovered {ScannerCount} scanner records.",
                        diagnostic.Protocol,
                        diagnostic.DeviceCount);
                }
            }

            await PublishLocalInventoryAsync(snapshot, cancellationToken);
            _logger.LogInformation(
                "Scanner discovery cycle completed in {DurationMilliseconds} ms.",
                _timeProvider.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Scanner discovery cycle failed; heartbeat processing and the runtime will continue.");
        }
    }

    private async Task PublishLocalInventoryAsync(
        ScannerDiscoverySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
            _options.ScannerInventoryPublishMode,
            AtlasEdgeOptions.ScannerInventoryPublishModeDisabled,
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(
            _options.ScannerInventoryPublishMode,
            AtlasEdgeOptions.ScannerInventoryPublishModeQueueOnly,
            StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Scanner inventory publication mode is not locally safe; inventory was not queued.");
            return;
        }

        var identity = _identityState.Current;
        if (identity is null)
        {
            _logger.LogInformation(
                "Scanner inventory changed but runtime identity is not available; local publication is deferred.");
            return;
        }

        var result = await _queue.EnqueueInventoryAsync(
            _eventBuilder.Build(snapshot, identity),
            cancellationToken);
        _logger.LogInformation(result.WasQueued
            ? "Scanner inventory changed; queued local inventory event."
            : "Scanner inventory unchanged; no event created.");
    }
}
