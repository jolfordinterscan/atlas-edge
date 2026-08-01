using Atlas.Edge.Core;
using Microsoft.Extensions.Logging;

namespace Atlas.Edge.Transport;

public sealed class NullEventTransport : IEventTransport
{
    private readonly ILogger<NullEventTransport> _logger;

    public NullEventTransport(ILogger<NullEventTransport> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(IReadOnlyList<QueueItem<AgentHeartbeatEvent>> batch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var item in batch)
        {
            _logger.LogInformation(
                "Null transport received event {EventType} ({EventId}) for agent {AgentId}.",
                item.Payload.EventType,
                item.Payload.EventId,
                item.Payload.AgentId);
        }

        return Task.CompletedTask;
    }
}