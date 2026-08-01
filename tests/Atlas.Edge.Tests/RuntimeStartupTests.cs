using Atlas.Edge.Configuration;
using Atlas.Edge.Core;
using Atlas.Edge.Queue;
using Atlas.Edge.Runtime;
using Atlas.Edge.Security;
using Atlas.Edge.Telemetry;
using Atlas.Edge.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atlas.Edge.Tests;

public sealed class RuntimeStartupTests
{
    [Fact]
    public async Task Host_StartsAndStopsCleanly()
    {
        var credentialStore = new InMemoryCredentialStore();
        var enrollmentClient = new StubEnrollmentClient();

        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddOptions<AtlasEdgeOptions>().Configure(options =>
        {
            options.AgentId = "dev-agent-placeholder";
            options.WorkstationId = "dev-workstation-placeholder";
            options.TenantBinding = "tenant-placeholder";
            options.IngestionUrl = "https://example.invalid/atlas-ingestion-placeholder";
            options.EnrollmentUrl = "https://localhost:7143/";
            options.EnrollmentCode = "DEV-ENROLL-ATLAS-EDGE";
            options.TransportMode = AtlasEdgeOptions.TransportModeNull;
            options.HeartbeatIntervalSeconds = 1;
            options.HttpTimeoutSeconds = 5;
            options.QueueBatchSize = 10;
            options.SiteTimezone = "UTC";
            options.EnvironmentName = "Test";
        });
        builder.Services.AddSingleton<RuntimeState>();
        builder.Services.AddSingleton(new EndpointSecurityPolicy(allowInsecureHttp: false));
        builder.Services.AddSingleton<DevelopmentIdentityProvider>();
        builder.Services.AddSingleton<RuntimeTransportCredentialProvider>();
        builder.Services.AddSingleton<ICredentialStore>(credentialStore);
        builder.Services.AddSingleton<Atlas.Edge.Enrollment.IEnrollmentClient>(enrollmentClient);
        builder.Services.AddSingleton<HeartbeatEventBuilder>();
        builder.Services.AddSingleton<IEventQueue, InMemoryEventQueue>();
        builder.Services.AddSingleton<IEventTransport, NullEventTransport>();
        builder.Services.AddSingleton<ITransportCredentialProvider>(sp => sp.GetRequiredService<RuntimeTransportCredentialProvider>());
        builder.Services.AddLogging();
        builder.Services.AddHostedService<Worker>();

        using var host = builder.Build();

        await host.StartAsync();
        await Task.Delay(100, CancellationToken.None);
        await host.StopAsync();

        var state = host.Services.GetRequiredService<RuntimeState>();
        Assert.NotEqual(Atlas.Edge.Core.RuntimeStatus.Starting, state.Current.Status);
    }

    [Fact]
    public async Task RuntimeRestart_ReusesStoredIdentity_AndSkipsEnrollment()
    {
        var credentialStore = new InMemoryCredentialStore();
        var enrollmentClient = new StubEnrollmentClient();

        using (var host = BuildHttpModeHost(credentialStore, enrollmentClient))
        {
            await host.StartAsync();
            await Task.Delay(200, CancellationToken.None);
            await host.StopAsync();
        }

        using (var host = BuildHttpModeHost(credentialStore, enrollmentClient))
        {
            await host.StartAsync();
            await Task.Delay(200, CancellationToken.None);
            await host.StopAsync();
        }

        Assert.Equal(1, enrollmentClient.Calls);
        Assert.NotNull(await credentialStore.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Host_StopAsync_TransitionsToStoppingState()
    {
        var credentialStore = new InMemoryCredentialStore();
        var enrollmentClient = new StubEnrollmentClient();

        using var host = BuildHttpModeHost(credentialStore, enrollmentClient);

        await host.StartAsync();
        await host.StopAsync();

        var state = host.Services.GetRequiredService<RuntimeState>();
        Assert.Equal(Atlas.Edge.Core.RuntimeStatus.Stopping, state.Current.Status);
    }

    private static IHost BuildHttpModeHost(InMemoryCredentialStore store, StubEnrollmentClient enrollmentClient)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddOptions<AtlasEdgeOptions>().Configure(options =>
        {
            options.AgentId = "dev-agent-placeholder";
            options.WorkstationId = "dev-workstation-placeholder";
            options.TenantBinding = "tenant-dev-a";
            options.IngestionUrl = "https://localhost:7143/";
            options.EnrollmentUrl = "https://localhost:7143/";
            options.EnrollmentCode = "DEV-ENROLL-ATLAS-EDGE";
            options.TransportMode = AtlasEdgeOptions.TransportModeHttp;
            options.HeartbeatIntervalSeconds = 1;
            options.HttpTimeoutSeconds = 5;
            options.QueueBatchSize = 10;
            options.SiteTimezone = "UTC";
            options.EnvironmentName = "Test";
        });

        builder.Services.AddSingleton<RuntimeState>();
        builder.Services.AddSingleton(new EndpointSecurityPolicy(allowInsecureHttp: false));
        builder.Services.AddSingleton<DevelopmentIdentityProvider>();
        builder.Services.AddSingleton<RuntimeTransportCredentialProvider>();
        builder.Services.AddSingleton<ICredentialStore>(store);
        builder.Services.AddSingleton<Atlas.Edge.Enrollment.IEnrollmentClient>(enrollmentClient);
        builder.Services.AddSingleton<HeartbeatEventBuilder>();
        builder.Services.AddSingleton<IEventQueue, InMemoryEventQueue>();
        builder.Services.AddSingleton<ITransportCredentialProvider>(sp => sp.GetRequiredService<RuntimeTransportCredentialProvider>());
        builder.Services.AddSingleton<IEventTransport, StubSuccessTransport>();
        builder.Services.AddLogging();
        builder.Services.AddHostedService<Worker>();

        return builder.Build();
    }

    private sealed class StubSuccessTransport : IEventTransport
    {
        public Task<TransportSendResult> SendAsync(IReadOnlyList<Atlas.Edge.Core.QueueItem<Atlas.Edge.Core.AgentHeartbeatEvent>> batch, CancellationToken cancellationToken)
        {
            var accepted = batch.Select(item => item.Payload.EventId).ToArray();
            return Task.FromResult(TransportSendResult.Success(accepted));
        }
    }

    private sealed class StubEnrollmentClient : Atlas.Edge.Enrollment.IEnrollmentClient
    {
        public int Calls { get; private set; }

        public Task<Atlas.Edge.Enrollment.EnrollmentResult> EnrollAsync(Atlas.Edge.Enrollment.EnrollmentRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;

            var response = new Atlas.Edge.Enrollment.EnrollmentResponse(
                $"agent-{Guid.NewGuid():N}",
                $"device-{Guid.NewGuid():N}",
                "tenant-dev-a",
                "https://localhost:7143/",
                "UTC",
                "token-123",
                "refresh-token-placeholder",
                DateTimeOffset.UtcNow.AddHours(1));

            return Task.FromResult(Atlas.Edge.Enrollment.EnrollmentResult.Success(response));
        }
    }

    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        private StoredEdgeCredentials? _credentials;

        public Task<StoredEdgeCredentials?> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_credentials);
        }

        public Task SaveAsync(StoredEdgeCredentials credentials, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _credentials = credentials;
            return Task.CompletedTask;
        }
    }
}
