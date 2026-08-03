using Atlas.Edge.Core;
using Atlas.Edge.Queue;
using Atlas.Edge.Runtime;

namespace Atlas.Edge.Tests;

public sealed class DurableEventQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Heartbeat_RetryAndEventIdentitySurviveRestart()
    {
        using var context = new QueueContext();
        var first = context.CreateQueue();
        var heartbeat = Heartbeat("event-1");
        var receipt = await first.EnqueueAsync(heartbeat, CancellationToken.None);
        var initial = Assert.Single(await first.PeekBatchAsync(10, CancellationToken.None));
        await first.RetryAsync([receipt], Now.AddSeconds(5), CancellationToken.None);

        context.Time.Advance(TimeSpan.FromSeconds(6));
        var restarted = context.CreateQueue();
        var replay = Assert.Single(await restarted.PeekBatchAsync(10, CancellationToken.None));

        Assert.Equal(receipt, replay.ReceiptId);
        Assert.Equal(heartbeat.EventId, replay.Payload.EventId);
        Assert.Equal(initial.AttemptCount + 1, replay.AttemptCount);
    }

    [Fact]
    public async Task Inventory_SurvivesRestartAndAcknowledgedFingerprintSuppressesDuplicates()
    {
        using var context = new QueueContext();
        var first = context.CreateQueue();
        var inventory = Inventory("inventory-1", new string('a', 64));
        Assert.True((await first.EnqueueInventoryAsync(inventory, CancellationToken.None)).WasQueued);

        var restarted = context.CreateQueue();
        Assert.Equal(inventory.EventId, (await restarted.GetLatestInventoryAsync(CancellationToken.None))?.EventId);
        await restarted.AcknowledgeInventoryAsync(inventory.EventId, CancellationToken.None);

        var afterAcknowledgement = context.CreateQueue();
        var duplicate = await afterAcknowledgement.EnqueueInventoryAsync(
            Inventory("inventory-2", inventory.InventoryVersion),
            CancellationToken.None);

        Assert.False(duplicate.WasQueued);
        Assert.Null(await afterAcknowledgement.GetLatestInventoryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task QueueCapacity_RemovesOldestPendingHeartbeatAndRemainsBounded()
    {
        using var context = new QueueContext(maximumPendingEvents: 2);
        var queue = context.CreateQueue();
        await queue.EnqueueAsync(Heartbeat("oldest"), CancellationToken.None);
        context.Time.Advance(TimeSpan.FromSeconds(1));
        await queue.EnqueueAsync(Heartbeat("middle"), CancellationToken.None);
        context.Time.Advance(TimeSpan.FromSeconds(1));
        await queue.EnqueueAsync(Heartbeat("newest"), CancellationToken.None);

        var batch = await queue.PeekBatchAsync(10, CancellationToken.None);

        Assert.Equal(["middle", "newest"], batch.Select(item => item.Payload.EventId));
        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public async Task PersistedQueue_ContainsEventsButNoCredentialOrEnrollmentFields()
    {
        using var context = new QueueContext();
        var queue = context.CreateQueue();
        await queue.EnqueueAsync(Heartbeat("event-1"), CancellationToken.None);
        await queue.EnqueueInventoryAsync(Inventory("inventory-1", new string('b', 64)), CancellationToken.None);

        var json = await File.ReadAllTextAsync(context.Path);

        Assert.Contains("agent.heartbeat", json, StringComparison.Ordinal);
        Assert.Contains("scanner.inventory", json, StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enrollmentCode", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InMemoryQueue_SuppressesInventoryAfterAcknowledgement()
    {
        var queue = new InMemoryEventQueue();
        var inventory = Inventory("inventory-1", new string('c', 64));
        await queue.EnqueueInventoryAsync(inventory, CancellationToken.None);
        await queue.AcknowledgeInventoryAsync(inventory.EventId, CancellationToken.None);

        var result = await queue.EnqueueInventoryAsync(
            Inventory("inventory-2", inventory.InventoryVersion),
            CancellationToken.None);

        Assert.False(result.WasQueued);
        Assert.Null(await queue.GetLatestInventoryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ForcedReconciliation_QueuesAcknowledgedInventoryButNeverReplacesPendingInventory()
    {
        using var context = new QueueContext();
        var queue = context.CreateQueue();
        var first = Inventory("inventory-1", new string('d', 64));
        await queue.EnqueueInventoryAsync(first, CancellationToken.None);
        await queue.AcknowledgeInventoryAsync(first.EventId, CancellationToken.None);

        var reconciliation = Inventory("inventory-2", first.InventoryVersion);
        Assert.True((await queue.EnqueueInventoryAsync(
            reconciliation,
            true,
            CancellationToken.None)).WasQueued);
        Assert.False((await queue.EnqueueInventoryAsync(
            Inventory("inventory-3", first.InventoryVersion),
            true,
            CancellationToken.None)).WasQueued);
        Assert.Equal(reconciliation.EventId,
            (await queue.GetLatestInventoryAsync(CancellationToken.None))?.EventId);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(20, 300)]
    public void RetryBackoff_IsExponentialAndBounded(int attempt, int expectedSeconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            Worker.GetQueueRetryDelay(attempt, 5, 300));
    }

    private static AgentHeartbeatEvent Heartbeat(string eventId) => new(
        eventId,
        "agent.heartbeat",
        "1.0",
        Now,
        Now,
        "agent-1",
        "workstation-1",
        "tenant-1",
        "runtime.foundation",
        null,
        "Test");

    private static ScannerInventoryEvent Inventory(string eventId, string version) => new(
        eventId,
        "scanner.inventory",
        "1.0",
        Now,
        "agent-1",
        "workstation-1",
        version,
        0,
        []);

    private sealed class QueueContext : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"atlas-edge-queue-tests-{Guid.NewGuid():N}");
        private readonly int _maximumPendingEvents;

        public QueueContext(int maximumPendingEvents = 100) =>
            _maximumPendingEvents = maximumPendingEvents;

        public string Path => System.IO.Path.Combine(_directory, "outbound-events.json");

        public ManualTimeProvider Time { get; } = new(Now);

        public JsonFileEventQueue CreateQueue() => new(
            Path,
            _maximumPendingEvents,
            TimeSpan.FromDays(7),
            Time);

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
    }
}
