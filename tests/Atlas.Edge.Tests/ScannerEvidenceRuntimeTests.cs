using System.Collections.Immutable;
using Atlas.Edge.Configuration;
using Atlas.Edge.Runtime;
using Atlas.Edge.ScannerEvidence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Edge.Tests;

public sealed class ScannerEvidenceRuntimeTests
{
    [Fact]
    public async Task HostedService_PublishesStartupEvidenceSnapshot()
    {
        var snapshot = new ScannerEvidenceCollectionSnapshot(
            DateTimeOffset.UtcNow,
            ImmutableArray<ScannerEvidenceSnapshot>.Empty,
            ImmutableArray<EvidenceProviderDiagnostic>.Empty);
        var state = new ScannerEvidenceState();
        var service = new ScannerEvidenceHostedService(
            new StaticManager(snapshot),
            state,
            NullLogger<ScannerEvidenceHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);

        Assert.Same(snapshot, state.Current);
    }

    [Fact]
    public async Task HostedService_ContinuesWithoutPublishingWhenProviderFails()
    {
        var state = new ScannerEvidenceState();
        var service = new ScannerEvidenceHostedService(
            new ThrowingManager(),
            state,
            NullLogger<ScannerEvidenceHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);

        Assert.Null(state.Current);
    }

    [Fact]
    public void Registration_IsDisabledByDefaultAndGatesMockToDevelopment()
    {
        var disabled = CreateServices();
        var production = CreateServices();
        var development = CreateServices();

        Assert.False(disabled.AddScannerEvidenceStartup(new AtlasEdgeOptions()));
        Assert.False(production.AddScannerEvidenceStartup(new AtlasEdgeOptions
        {
            ScannerEvidenceEnabled = true,
            ScannerEvidenceMode = AtlasEdgeOptions.ScannerEvidenceModeMock,
            EnvironmentName = "Production"
        }));
        Assert.True(development.AddScannerEvidenceStartup(new AtlasEdgeOptions
        {
            ScannerEvidenceEnabled = true,
            ScannerEvidenceMode = AtlasEdgeOptions.ScannerEvidenceModeMock,
            EnvironmentName = "Development"
        }));

        Assert.DoesNotContain(production, descriptor => descriptor.ServiceType == typeof(IScannerEvidenceProvider));
        using var provider = development.BuildServiceProvider();
        Assert.IsType<DevelopmentMockEvidenceProvider>(provider.GetRequiredService<IScannerEvidenceProvider>());
    }

    [Fact]
    public void HostedService_HasNoQueueTransportOrKnowledgeDependency()
    {
        var parameters = typeof(ScannerEvidenceHostedService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.FullName)
            .ToArray();

        Assert.DoesNotContain(parameters, value => value?.Contains("Atlas.Edge.Queue", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(parameters, value => value?.Contains("Atlas.Edge.Transport", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(parameters, value => value?.Contains("Atlas.Edge.Knowledge", StringComparison.Ordinal) == true);
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        return services;
    }

    private sealed class StaticManager : IScannerEvidenceManager
    {
        private readonly ScannerEvidenceCollectionSnapshot _snapshot;

        public StaticManager(ScannerEvidenceCollectionSnapshot snapshot) => _snapshot = snapshot;

        public Task<ScannerEvidenceCollectionSnapshot> CollectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot);
    }

    private sealed class ThrowingManager : IScannerEvidenceManager
    {
        public Task<ScannerEvidenceCollectionSnapshot> CollectAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Evidence provider failure.");
    }
}
