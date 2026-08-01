using Atlas.Edge.Configuration;
using Atlas.Edge.Queue;
using Atlas.Edge.Runtime;
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
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddOptions<AtlasEdgeOptions>().Configure(options =>
        {
            options.AgentId = "dev-agent-placeholder";
            options.WorkstationId = "dev-workstation-placeholder";
            options.TenantBinding = "tenant-placeholder";
            options.IngestionUrl = "https://example.invalid/atlas-ingestion-placeholder";
            options.HeartbeatIntervalSeconds = 1;
            options.QueueBatchSize = 10;
            options.EnvironmentName = "Test";
        });
        builder.Services.AddSingleton<RuntimeState>();
        builder.Services.AddSingleton<DevelopmentIdentityProvider>();
        builder.Services.AddSingleton<HeartbeatEventBuilder>();
        builder.Services.AddSingleton<IEventQueue, InMemoryEventQueue>();
        builder.Services.AddSingleton<IEventTransport, NullEventTransport>();
        builder.Services.AddLogging();
        builder.Services.AddHostedService<Worker>();

        using var host = builder.Build();

        await host.StartAsync();
        await Task.Delay(100, CancellationToken.None);
        await host.StopAsync();

        var state = host.Services.GetRequiredService<RuntimeState>();
        Assert.NotEqual(Atlas.Edge.Core.RuntimeStatus.Starting, state.Current.Status);
    }
}