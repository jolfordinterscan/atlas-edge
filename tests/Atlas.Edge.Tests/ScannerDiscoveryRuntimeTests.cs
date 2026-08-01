using Atlas.Edge.Runtime;
using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.Configuration;
using Atlas.Edge.Core;
using Atlas.Edge.Queue;
using Atlas.Edge.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Tests;

public sealed class ScannerDiscoveryRuntimeTests
{
    [Fact]
    public async Task HostedService_PublishesStartupInventorySnapshot()
    {
        var scanner = new DiscoveredScanner(
            "scanner-test",
            "Acme",
            "ScanPro",
            "SERIAL-1",
            "1.0",
            "USB",
            true,
            true,
            true,
            ["duplex"],
            [new ScannerDriver("Driver", "1.0", "Acme")],
            ScannerOnlineStatus.Online,
            [ScannerProtocol.Mock]);
        var snapshot = new ScannerDiscoverySnapshot(
            DateTimeOffset.UtcNow,
            [scanner],
            [new ScannerAdapterDiagnostic(ScannerProtocol.Mock, true, 1, null)]);
        var inventory = new ScannerInventoryState();
        var queue = new InMemoryEventQueue();
        var hostedService = CreateHostedService(new StaticDiscoveryService(snapshot), inventory, queue);

        await hostedService.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => inventory.Current is not null);
        await hostedService.StopAsync(CancellationToken.None);

        Assert.Same(snapshot, inventory.Current);
        Assert.Single(inventory.Current!.Scanners);
        Assert.NotNull(await queue.GetLatestInventoryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task HostedService_LeavesRuntimeUsableWhenDiscoveryFails()
    {
        var inventory = new ScannerInventoryState();
        var hostedService = CreateHostedService(
            new ThrowingDiscoveryService(),
            inventory,
            new InMemoryEventQueue());

        await hostedService.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await hostedService.StopAsync(CancellationToken.None);

        Assert.Null(inventory.Current);
    }

    [Fact]
    public async Task TransportMode_AcknowledgesAcceptedChangedInventory()
    {
        var snapshot = CreateSnapshot("fi-8170");
        var queue = new InMemoryEventQueue();
        var transport = new RecordingTransport(TransportFailureKind.None);
        var service = CreateHostedService(
            new StaticDiscoveryService(snapshot),
            new ScannerInventoryState(),
            queue,
            AtlasEdgeOptions.ScannerInventoryPublishModeTransport,
            transport);

        await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(1, transport.InventorySendCount);
        Assert.Null(await queue.GetLatestInventoryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TransportMode_RetainsTransientFailureAndRetriesWithoutNewSnapshot()
    {
        var snapshot = CreateSnapshot("fi-8170");
        var queue = new InMemoryEventQueue();
        var transport = new RecordingTransport(TransportFailureKind.Retryable);
        var service = CreateHostedService(
            new StaticDiscoveryService(snapshot),
            new ScannerInventoryState(),
            queue,
            AtlasEdgeOptions.ScannerInventoryPublishModeTransport,
            transport);

        await service.RunCycleAsync(CancellationToken.None);
        await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(2, transport.InventorySendCount);
        Assert.NotNull(await queue.GetLatestInventoryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TransportMode_DropsPermanentInventoryRejectionWithoutTouchingHeartbeatQueue()
    {
        var queue = new InMemoryEventQueue();
        var heartbeat = new AgentHeartbeatEvent(
            "heartbeat", "agent.heartbeat", "1.0", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            "agent-test", "workstation-test", "tenant-test", "runtime", null, "Running");
        await queue.EnqueueAsync(heartbeat, CancellationToken.None);
        var service = CreateHostedService(
            new StaticDiscoveryService(CreateSnapshot("fi-8170")),
            new ScannerInventoryState(),
            queue,
            AtlasEdgeOptions.ScannerInventoryPublishModeTransport,
            new RecordingTransport(TransportFailureKind.NonRetryable));

        await service.RunCycleAsync(CancellationToken.None);

        Assert.Null(await queue.GetLatestInventoryAsync(CancellationToken.None));
        Assert.Single(await queue.PeekBatchAsync(10, CancellationToken.None));
    }

    private static ScannerDiscoveryHostedService CreateHostedService(
        IScannerDiscoveryService discoveryService,
        ScannerInventoryState inventory,
        IEventQueue queue,
        string publishMode = AtlasEdgeOptions.ScannerInventoryPublishModeQueueOnly,
        IEventTransport? transport = null)
    {
        var identityState = new RuntimeIdentityState();
        identityState.Update(new AgentIdentity(
            "agent-test",
            "workstation-test",
            "tenant-test",
            "Test",
            false,
            DateTimeOffset.UtcNow));
        var options = Options.Create(new AtlasEdgeOptions
        {
            ScannerDiscoveryStartupDelaySeconds = 0,
            ScannerDiscoveryIntervalSeconds = 30,
            ScannerInventoryPublishMode = publishMode
        });
        return new ScannerDiscoveryHostedService(
            discoveryService,
            new ScannerInventoryEventBuilder(),
            inventory,
            identityState,
            queue,
            transport ?? new NullEventTransport(NullLogger<NullEventTransport>.Instance),
            options,
            TimeProvider.System,
            NullLogger<ScannerDiscoveryHostedService>.Instance);
    }

    private static ScannerDiscoverySnapshot CreateSnapshot(string model)
    {
        var now = DateTimeOffset.UtcNow;
        return new ScannerDiscoverySnapshot(
            now,
            [new DiscoveredScanner(
                "scanner-test", "FUJITSU", model, null, null, "USB", true, true, true,
                ["Unknown"], [], ScannerOnlineStatus.Unknown, [ScannerProtocol.Wia])],
            [new ScannerAdapterDiagnostic(ScannerProtocol.Wia, true, 1, null)]);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!predicate() && DateTimeOffset.UtcNow < timeout)
        {
            await Task.Delay(10);
        }
    }

    private sealed class StaticDiscoveryService : IScannerDiscoveryService
    {
        private readonly ScannerDiscoverySnapshot _snapshot;

        public StaticDiscoveryService(ScannerDiscoverySnapshot snapshot) => _snapshot = snapshot;

        public Task<ScannerDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot);
    }

    private sealed class ThrowingDiscoveryService : IScannerDiscoveryService
    {
        public Task<ScannerDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Platform failure");
    }

    private sealed class RecordingTransport(TransportFailureKind failureKind) : IEventTransport
    {
        public int InventorySendCount { get; private set; }

        public Task<TransportSendResult> SendAsync(
            IReadOnlyList<QueueItem<AgentHeartbeatEvent>> batch,
            CancellationToken cancellationToken) =>
            Task.FromResult(TransportSendResult.Success(batch.Select(item => item.Payload.EventId)));

        public Task<TransportSendResult> SendInventoryAsync(
            ScannerInventoryEvent inventory,
            CancellationToken cancellationToken)
        {
            InventorySendCount++;
            return Task.FromResult(failureKind switch
            {
                TransportFailureKind.None => TransportSendResult.Success([inventory.EventId]),
                TransportFailureKind.NonRetryable => TransportSendResult.NonRetryable("invalid_scanner_inventory"),
                _ => TransportSendResult.Retryable("temporary_failure")
            });
        }
    }
}
