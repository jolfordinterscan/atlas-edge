using Atlas.Edge.Configuration;
using Atlas.Edge.Core;
using Atlas.Edge.Enrollment;
using Atlas.Edge.Queue;
using Atlas.Edge.Runtime;
using Atlas.Edge.Security;
using Atlas.Edge.Telemetry;
using Atlas.Edge.Transport;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "ATLAS_EDGE_");

var optionsSectionPath = AtlasEdgeOptions.SectionName;
var enrollmentUrlValue = builder.Configuration[$"{optionsSectionPath}:EnrollmentUrl"];
if (!Uri.TryCreate(enrollmentUrlValue, UriKind.Absolute, out var enrollmentUrl))
{
    enrollmentUrl = new Uri("https://localhost:7143/");
}

var httpTimeoutSecondsValue = builder.Configuration[$"{optionsSectionPath}:HttpTimeoutSeconds"];
var timeoutSeconds = int.TryParse(httpTimeoutSecondsValue, out var parsedTimeout) && parsedTimeout > 0
    ? parsedTimeout
    : 15;

var configuredTransportMode = builder.Configuration[$"{optionsSectionPath}:TransportMode"] ?? AtlasEdgeOptions.TransportModeNull;

builder.Services.AddAtlasEdgeConfiguration(builder.Configuration);
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AtlasEdgeOptions>>().Value;
    var allowInsecureHttp = EndpointSecurityPolicy.IsDevelopmentOverrideEnabled(
        options.EnvironmentName,
        options.AllowInsecureHttpForDevelopment);
    return new EndpointSecurityPolicy(allowInsecureHttp);
});
builder.Services.AddSingleton<RuntimeState>();
builder.Services.AddSingleton<DevelopmentIdentityProvider>();
builder.Services.AddSingleton<RuntimeTransportCredentialProvider>();
builder.Services.AddSingleton<ITransportCredentialProvider>(sp => sp.GetRequiredService<RuntimeTransportCredentialProvider>());
builder.Services.AddSingleton<HeartbeatEventBuilder>();
builder.Services.AddSingleton<IEventQueue, InMemoryEventQueue>();

if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<ICredentialStore, WindowsCredentialStorePlaceholder>();
}
else
{
    builder.Services.AddSingleton<ICredentialStore, MacDevelopmentCredentialStore>();
}

builder.Services.AddHttpClient<IEnrollmentClient, HttpEnrollmentClient>(client =>
{
    client.BaseAddress = enrollmentUrl;
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});

builder.Services.AddHttpClient<HttpEventTransport>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});

if (string.Equals(configuredTransportMode, AtlasEdgeOptions.TransportModeHttp, StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IEventTransport>(sp => sp.GetRequiredService<HttpEventTransport>());
}
else
{
    builder.Services.AddSingleton<IEventTransport, NullEventTransport>();
}

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
