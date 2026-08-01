using Atlas.Edge.Configuration;
using Atlas.Edge.Core;
using Atlas.Edge.Queue;
using Atlas.Edge.Telemetry;
using Atlas.Edge.Transport;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Runtime;

public sealed class Worker : BackgroundService
{
    private readonly DevelopmentIdentityProvider _identityProvider;
    private readonly HeartbeatEventBuilder _heartbeatEventBuilder;
    private readonly ILogger<Worker> _logger;
    private readonly AtlasEdgeOptions _options;
    private readonly IEventQueue _queue;
    private readonly RuntimeState _runtimeState;
    private readonly IEventTransport _transport;

    public Worker(
        IOptions<AtlasEdgeOptions> options,
        DevelopmentIdentityProvider identityProvider,
        HeartbeatEventBuilder heartbeatEventBuilder,
        IEventQueue queue,
        IEventTransport transport,
        RuntimeState runtimeState,
        ILogger<Worker> logger)
    {
        _identityProvider = identityProvider;
        _heartbeatEventBuilder = heartbeatEventBuilder;
        _queue = queue;
        _transport = transport;
        _runtimeState = runtimeState;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _runtimeState.Update(RuntimeStatus.Starting, "Runtime starting.");
        _logger.LogInformation(
            "Atlas Edge runtime starting for environment {EnvironmentName}.",
            _options.EnvironmentName);

        var identity = _identityProvider.Create(_options);
        _runtimeState.Update(RuntimeStatus.Running, "Runtime running.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessHeartbeatAsync(identity, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _runtimeState.Update(RuntimeStatus.Degraded, "Heartbeat cycle failed.", ex.Message);
                _logger.LogError(ex, "Heartbeat cycle failed and will be retried without stopping the runtime.");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(_options.HeartbeatIntervalSeconds, 5)), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _runtimeState.Update(RuntimeStatus.Stopping, "Runtime stopping.");
        _logger.LogInformation("Atlas Edge runtime stopping gracefully.");
        return base.StopAsync(cancellationToken);
    }

    private async Task ProcessHeartbeatAsync(AgentIdentity identity, CancellationToken cancellationToken)
    {
        var heartbeat = _heartbeatEventBuilder.Build(identity, _options, DateTimeOffset.UtcNow);
        var receiptId = await _queue.EnqueueAsync(heartbeat, cancellationToken);

        _logger.LogInformation(
            "Generated heartbeat event {EventId} and queued it as receipt {ReceiptId}.",
            heartbeat.EventId,
            receiptId);

        var batch = await _queue.PeekBatchAsync(_options.QueueBatchSize, cancellationToken);
        if (batch.Count == 0)
        {
            return;
        }

        await _transport.SendAsync(batch, cancellationToken);
        await _queue.AcknowledgeAsync(batch.Select(item => item.ReceiptId), cancellationToken);

        var queueHealth = await _queue.GetHealthAsync(cancellationToken);
        _runtimeState.Update(RuntimeStatus.Running, "Heartbeat cycle completed.");

        _logger.LogInformation(
            "Heartbeat cycle completed with queue pending count {PendingCount} and in-flight count {InFlightCount}.",
            queueHealth.PendingCount,
            queueHealth.InFlightCount);
    }
}
