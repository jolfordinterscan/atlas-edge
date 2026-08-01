using Atlas.Edge.Core;
using Atlas.Edge.Queue;

namespace Atlas.Edge.Tests;

public sealed class QueueTests
{
    [Fact]
    public async Task Enqueue_Peek_AndAcknowledge_WorkAsExpected()
    {
        var queue = new InMemoryEventQueue();
        var heartbeat = new AgentHeartbeatEvent(
            Guid.NewGuid().ToString("N"),
            "agent.heartbeat",
            "1.0",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "agent-1",
            "workstation-1",
            "tenant-a",
            "runtime.foundation",
            null,
            "Development");

        var receiptId = await queue.EnqueueAsync(heartbeat, CancellationToken.None);
        var batch = await queue.PeekBatchAsync(10, CancellationToken.None);

        Assert.Single(batch);
        Assert.Equal(receiptId, batch[0].ReceiptId);

        await queue.AcknowledgeAsync(new[] { receiptId }, CancellationToken.None);

        var health = await queue.GetHealthAsync(CancellationToken.None);
        Assert.Equal(0, health.PendingCount);
        Assert.Equal(0, health.InFlightCount);
    }
}