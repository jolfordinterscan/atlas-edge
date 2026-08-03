using Atlas.Edge.Configuration;
using Atlas.Edge.Core;
using Atlas.Edge.Enrollment;
using Atlas.Edge.Queue;
using Atlas.Edge.Runtime;
using Atlas.Edge.Patterns;
using Atlas.Edge.ScannerConnectors;
using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.ScannerEvidence;
using Atlas.Edge.ScannerHealth;
using Atlas.Edge.Security;
using Atlas.Edge.Telemetry;
using Atlas.Edge.Transport;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "ATLAS_EDGE_");

var optionsSectionPath = AtlasEdgeOptions.SectionName;
var enrollmentUrlValue = builder.Configuration[$"{optionsSectionPath}:EnrollmentUrl"];
if (!Uri.TryCreate(enrollmentUrlValue, UriKind.Absolute, out var enrollmentUrl))
{
    enrollmentUrl = new Uri("https://atlas-web-staging-732a.up.railway.app/");
}

var httpTimeoutSecondsValue = builder.Configuration[$"{optionsSectionPath}:HttpTimeoutSeconds"];
var timeoutSeconds = int.TryParse(httpTimeoutSecondsValue, out var parsedTimeout) && parsedTimeout > 0
    ? parsedTimeout
    : 15;

var configuredTransportMode = builder.Configuration[$"{optionsSectionPath}:TransportMode"] ?? AtlasEdgeOptions.TransportModeNull;

builder.Services.AddAtlasEdgeConfiguration(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AtlasEdgeOptions>>().Value;
    var allowInsecureHttp = EndpointSecurityPolicy.IsDevelopmentOverrideEnabled(
        options.EnvironmentName,
        options.AllowInsecureHttpForDevelopment);
    return new EndpointSecurityPolicy(allowInsecureHttp);
});
builder.Services.AddSingleton<RuntimeState>();
builder.Services.AddSingleton<RuntimeIdentityState>();
builder.Services.AddSingleton<CredentialExpiryPolicy>();
builder.Services.AddSingleton<DevelopmentIdentityProvider>();
builder.Services.AddSingleton<RuntimeTransportCredentialProvider>();
builder.Services.AddSingleton<ITransportCredentialProvider>(sp => sp.GetRequiredService<RuntimeTransportCredentialProvider>());
builder.Services.AddSingleton<HeartbeatEventBuilder>();
builder.Services.AddSingleton<IEventQueue>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AtlasEdgeOptions>>().Value;
    if (string.Equals(options.TransportMode, AtlasEdgeOptions.TransportModeNull, StringComparison.OrdinalIgnoreCase))
    {
        return new InMemoryEventQueue();
    }

    var configuredPath = options.EventQueueStorePath;
    var path = string.IsNullOrWhiteSpace(configuredPath)
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "InterScan",
            "Atlas Edge",
            "queue",
            "outbound-events.json")
        : configuredPath;
    return new JsonFileEventQueue(
        path,
        options.QueueMaximumPendingEvents,
        TimeSpan.FromHours(options.QueueRetentionHours),
        sp.GetRequiredService<TimeProvider>());
});
builder.Services.AddSingleton<ScannerInventoryState>();
builder.Services.AddSingleton<ScannerHealthState>();
builder.Services.AddSingleton<ScannerConnectorState>();
builder.Services.AddSingleton<ScannerEvidenceState>();

var scannerDiscoveryEnabled = bool.TryParse(
    builder.Configuration[$"{optionsSectionPath}:ScannerDiscoveryEnabled"],
    out var parsedScannerDiscoveryEnabled)
        ? parsedScannerDiscoveryEnabled
        : true;
var scannerDiscoveryProvider = builder.Configuration[$"{optionsSectionPath}:ScannerDiscoveryProvider"] ??
    AtlasEdgeOptions.ScannerDiscoveryProviderPlatform;
var scannerDiscoveryProviders = builder.Configuration
    .GetSection($"{optionsSectionPath}:ScannerDiscoveryProviders")
    .Get<string[]>() ?? ["Wia"];
var configuredEnvironmentName = builder.Configuration[$"{optionsSectionPath}:EnvironmentName"] ?? "Development";
var mockScannerProviderAllowed = string.Equals(
        scannerDiscoveryProvider,
        AtlasEdgeOptions.ScannerDiscoveryProviderMock,
        StringComparison.OrdinalIgnoreCase) &&
    string.Equals(configuredEnvironmentName, "Development", StringComparison.OrdinalIgnoreCase);

if (scannerDiscoveryEnabled &&
    (!string.Equals(
        scannerDiscoveryProvider,
        AtlasEdgeOptions.ScannerDiscoveryProviderMock,
        StringComparison.OrdinalIgnoreCase) || mockScannerProviderAllowed))
{
    if (mockScannerProviderAllowed)
    {
        builder.Services.AddSingleton<IScannerDiscoveryAdapter, MockScannerDiscoveryAdapter>();
    }
    else
    {
        if (scannerDiscoveryProviders.Contains("Wia", StringComparer.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IWiaScannerSourceCatalog, WiaScannerSourceCatalog>();
            builder.Services.AddSingleton<IScannerDiscoveryAdapter, WiaScannerDiscoveryAdapter>();
        }

        if (scannerDiscoveryProviders.Contains("Twain", StringComparer.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<ITwainScannerSourceCatalog, TwainScannerSourceCatalog>();
            builder.Services.AddSingleton<IScannerDiscoveryAdapter, TwainScannerDiscoveryAdapter>();
        }

        if (scannerDiscoveryProviders.Contains("Isis", StringComparer.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IIsisScannerSourceCatalog, IsisScannerSourceCatalog>();
            builder.Services.AddSingleton<IScannerDiscoveryAdapter, IsisScannerDiscoveryAdapter>();
        }
    }

    builder.Services.AddSingleton<IScannerIdentityFactory, ScannerIdentityFactory>();
    builder.Services.AddSingleton<IPnpScannerMetadataCatalog, WindowsPnpScannerMetadataCatalog>();
    builder.Services.AddSingleton<IRegistryScannerMetadataCatalog, WindowsScannerRegistryMetadataCatalog>();
    builder.Services.AddSingleton<IScannerMetadataProvider, WindowsPnpScannerMetadataProvider>();
    builder.Services.AddSingleton<IScannerMetadataProvider, WindowsRegistryScannerMetadataProvider>();
    builder.Services.AddSingleton<IScannerMetadataEnricher>(sp =>
    {
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AtlasEdgeOptions>>().Value;
        return new ScannerMetadataEnricher(
            sp.GetServices<IScannerMetadataProvider>(),
            sp.GetRequiredService<TimeProvider>(),
            TimeSpan.FromSeconds(options.ScannerDiscoveryProviderTimeoutSeconds));
    });
    builder.Services.AddSingleton<IScannerInventoryEventBuilder, ScannerInventoryEventBuilder>();
    builder.Services.AddSingleton<IScannerDiscoveryService>(sp =>
    {
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AtlasEdgeOptions>>().Value;
        return new ScannerDiscoveryService(
            sp.GetServices<IScannerDiscoveryAdapter>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ScannerDiscoveryService>>(),
            sp.GetRequiredService<IScannerIdentityFactory>(),
            TimeSpan.FromSeconds(options.ScannerDiscoveryProviderTimeoutSeconds),
            sp.GetRequiredService<IScannerMetadataEnricher>());
    });
    builder.Services.AddHostedService<ScannerDiscoveryHostedService>();
}

var scannerHealthEnabled = bool.TryParse(
    builder.Configuration[$"{optionsSectionPath}:ScannerHealthEnabled"],
    out var parsedScannerHealthEnabled)
        ? parsedScannerHealthEnabled
        : true;
var scannerHealthProvider = builder.Configuration[$"{optionsSectionPath}:ScannerHealthProvider"] ??
    AtlasEdgeOptions.ScannerHealthProviderPlatform;
var mockScannerHealthProviderAllowed = string.Equals(
        scannerHealthProvider,
        AtlasEdgeOptions.ScannerHealthProviderMock,
        StringComparison.OrdinalIgnoreCase) &&
    string.Equals(configuredEnvironmentName, "Development", StringComparison.OrdinalIgnoreCase);

if (scannerHealthEnabled &&
    (!string.Equals(
        scannerHealthProvider,
        AtlasEdgeOptions.ScannerHealthProviderMock,
        StringComparison.OrdinalIgnoreCase) || mockScannerHealthProviderAllowed))
{
    if (mockScannerHealthProviderAllowed)
    {
        builder.Services.AddSingleton<IScannerHealthProvider, MockScannerHealthProvider>();
    }
    else
    {
        builder.Services.TryAddSingleton<IWiaScannerSourceCatalog, WiaScannerSourceCatalog>();
        builder.Services.TryAddSingleton<ITwainScannerSourceCatalog, TwainScannerSourceCatalog>();
        builder.Services.TryAddSingleton<IIsisScannerSourceCatalog, IsisScannerSourceCatalog>();
        builder.Services.AddSingleton<IScannerHealthProvider, WiaScannerHealthProvider>();
        builder.Services.AddSingleton<IScannerHealthProvider, TwainScannerHealthProvider>();
        builder.Services.AddSingleton<IScannerHealthProvider, IsisScannerHealthProvider>();
    }

    builder.Services.AddSingleton<HealthScoreCalculator>();
    builder.Services.AddSingleton<IScannerHealthService, ScannerHealthService>();
    builder.Services.AddHostedService<ScannerHealthHostedService>();
}

var scannerConnectorsEnabled = bool.TryParse(
    builder.Configuration[$"{optionsSectionPath}:ScannerConnectorsEnabled"],
    out var parsedScannerConnectorsEnabled) && parsedScannerConnectorsEnabled;
var scannerConnectorProvider = builder.Configuration[$"{optionsSectionPath}:ScannerConnectorProvider"] ??
    AtlasEdgeOptions.ScannerConnectorProviderPlatform;
builder.Services.AddScannerConnectorStartup(
    scannerConnectorsEnabled,
    scannerConnectorProvider,
    configuredEnvironmentName);

var scannerEvidenceOptions = builder.Configuration
    .GetSection(optionsSectionPath)
    .Get<AtlasEdgeOptions>() ?? new AtlasEdgeOptions();
builder.Services.AddScannerEvidenceStartup(scannerEvidenceOptions);
builder.Services.AddSingleton<PatternEngine>();
builder.Services.AddSingleton<MissionControlApplicationService>();

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

builder.Services.AddHttpClient<ITokenRefreshClient, HttpTokenRefreshClient>(client =>
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

builder.AddAtlasEdgeWindowsServiceFoundation();

var host = builder.Build();
await host.RunAsync();
