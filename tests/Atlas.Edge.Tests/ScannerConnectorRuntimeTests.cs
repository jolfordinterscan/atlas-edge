using System.Collections.Immutable;
using Atlas.Edge.Runtime;
using Atlas.Edge.ScannerConnectors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Edge.Tests;

public sealed class ScannerConnectorRuntimeTests
{
    [Fact]
    public async Task HostedService_PublishesStartupConnectorSnapshot()
    {
        var snapshot = new ScannerConnectorCollectionSnapshot(
            DateTimeOffset.UtcNow,
            ImmutableArray<ScannerConnectorSnapshot>.Empty,
            ImmutableArray<ConnectorDiagnostic>.Empty);
        var state = new ScannerConnectorState();
        var service = new ScannerConnectorHostedService(
            new StaticManager(snapshot),
            state,
            NullLogger<ScannerConnectorHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);

        Assert.Same(snapshot, state.Current);
    }

    [Fact]
    public async Task HostedService_DoesNotStopRuntimeOrPublishPartialStateAfterFailure()
    {
        var state = new ScannerConnectorState();
        var service = new ScannerConnectorHostedService(
            new ThrowingManager(),
            state,
            NullLogger<ScannerConnectorHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);

        Assert.Null(state.Current);
    }

    [Fact]
    public void HostedService_HasNoQueueOrTransportDependency()
    {
        var parameterTypes = typeof(ScannerConnectorHostedService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.FullName)
            .ToArray();

        Assert.DoesNotContain(parameterTypes, name => name?.Contains("Atlas.Edge.Queue", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(parameterTypes, name => name?.Contains("Atlas.Edge.Transport", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Registration_GatesMockConnectorToDevelopmentEnvironment()
    {
        var productionServices = CreateServices();
        var developmentServices = CreateServices();

        var productionAdded = productionServices.AddScannerConnectorStartup(
            enabled: true,
            provider: "Mock",
            environmentName: "Production");
        var developmentAdded = developmentServices.AddScannerConnectorStartup(
            enabled: true,
            provider: "Mock",
            environmentName: "Development");

        Assert.False(productionAdded);
        Assert.DoesNotContain(productionServices, descriptor => descriptor.ServiceType == typeof(IScannerConnector));
        Assert.True(developmentAdded);
        using var provider = developmentServices.BuildServiceProvider();
        Assert.IsType<DevelopmentMockScannerConnector>(provider.GetRequiredService<IScannerConnector>());
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        return services;
    }

    private sealed class StaticManager : IScannerConnectorManager
    {
        private readonly ScannerConnectorCollectionSnapshot _snapshot;

        public StaticManager(ScannerConnectorCollectionSnapshot snapshot) => _snapshot = snapshot;

        public Task<ScannerConnectorCollectionSnapshot> CollectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot);
    }

    private sealed class ThrowingManager : IScannerConnectorManager
    {
        public Task<ScannerConnectorCollectionSnapshot> CollectAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Platform connector failed.");
    }
}
