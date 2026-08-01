using Atlas.Edge.Core;

namespace Atlas.Edge.Queue;

public interface IEventQueue
{
    Task<string> EnqueueAsync(AgentHeartbeatEvent heartbeatEvent, CancellationToken cancellationToken);

    Task<IReadOnlyList<QueueItem<AgentHeartbeatEvent>>> PeekBatchAsync(int batchSize, CancellationToken cancellationToken);

    Task AcknowledgeAsync(IEnumerable<string> receiptIds, CancellationToken cancellationToken);

    Task RetryAsync(IEnumerable<string> receiptIds, DateTimeOffset availableAfterUtc, CancellationToken cancellationToken);

    Task<QueueHealth> GetHealthAsync(CancellationToken cancellationToken);
}