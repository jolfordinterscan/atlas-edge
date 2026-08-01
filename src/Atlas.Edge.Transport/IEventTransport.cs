using Atlas.Edge.Core;

namespace Atlas.Edge.Transport;

public interface IEventTransport
{
    Task SendAsync(IReadOnlyList<QueueItem<AgentHeartbeatEvent>> batch, CancellationToken cancellationToken);
}