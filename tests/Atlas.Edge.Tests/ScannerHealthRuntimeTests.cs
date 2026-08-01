using System.Collections.Immutable;
using Atlas.Edge.Runtime;
using Atlas.Edge.ScannerHealth;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Edge.Tests;

public sealed class ScannerHealthRuntimeTests
{
    [Fact]
    public async Task HostedService_PublishesStartupHealthCollection()
    {
        var snapshot = new ScannerHealthCollectionSnapshot(
            DateTimeOffset.UtcNow,
            ImmutableArray<ScannerHealthSnapshot>.Empty,
            ImmutableArray<ScannerHealthProviderDiagnostic>.Empty);
        var state = new ScannerHealthState();
        var hostedService = new ScannerHealthHostedService(
            new StaticHealthService(snapshot),
            state,
            NullLogger<ScannerHealthHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);

        Assert.Same(snapshot, state.Current);
    }

    [Fact]
    public async Task HostedService_DoesNotStopRuntimeWhenCollectionFails()
    {
        var state = new ScannerHealthState();
        var hostedService = new ScannerHealthHostedService(
            new ThrowingHealthService(),
            state,
            NullLogger<ScannerHealthHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);

        Assert.Null(state.Current);
    }

    private sealed class StaticHealthService : IScannerHealthService
    {
        private readonly ScannerHealthCollectionSnapshot _snapshot;

        public StaticHealthService(ScannerHealthCollectionSnapshot snapshot) => _snapshot = snapshot;

        public Task<ScannerHealthCollectionSnapshot> CollectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot);
    }

    private sealed class ThrowingHealthService : IScannerHealthService
    {
        public Task<ScannerHealthCollectionSnapshot> CollectAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provider failure");
    }
}
