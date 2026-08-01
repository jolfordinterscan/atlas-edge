using Atlas.Edge.Core;

namespace Atlas.Edge.Transport;

public interface IEventTransport
{
    Task<TransportSendResult> SendAsync(
        IReadOnlyList<QueueItem<AgentHeartbeatEvent>> batch,
        CancellationToken cancellationToken);
}