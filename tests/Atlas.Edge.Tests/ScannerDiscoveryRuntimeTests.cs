using Atlas.Edge.Runtime;
using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.Configuration;
using Atlas.Edge.Core;
using Atlas.Edge.Queue;
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

    private static ScannerDiscoveryHostedService CreateHostedService(
        IScannerDiscoveryService discoveryService,
        ScannerInventoryState inventory,
        IEventQueue queue)
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
            ScannerInventoryPublishMode = AtlasEdgeOptions.ScannerInventoryPublishModeQueueOnly
        });
        return new ScannerDiscoveryHostedService(
            discoveryService,
            new ScannerInventoryEventBuilder(),
            inventory,
            identityState,
            queue,
            options,
            TimeProvider.System,
            NullLogger<ScannerDiscoveryHostedService>.Instance);
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
}
