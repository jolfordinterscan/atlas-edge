using Atlas.Edge.Configuration;
using Atlas.Edge.Queue;
using Atlas.Edge.Runtime;
using Atlas.Edge.Telemetry;
using Atlas.Edge.Transport;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "ATLAS_EDGE_");

builder.Services.AddAtlasEdgeConfiguration(builder.Configuration);
builder.Services.AddSingleton<RuntimeState>();
builder.Services.AddSingleton<DevelopmentIdentityProvider>();
builder.Services.AddSingleton<HeartbeatEventBuilder>();
builder.Services.AddSingleton<IEventQueue, InMemoryEventQueue>();
builder.Services.AddSingleton<IEventTransport, NullEventTransport>();
builder.Services.AddHostedService<Worker>();

builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Atlas Edge Runtime";
});

var host = builder.Build();
host.Run();
