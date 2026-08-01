using System.Runtime.Versioning;
using Microsoft.Extensions.Logging.EventLog;

namespace Atlas.Edge.Runtime;

public static class WindowsServiceRegistration
{
    public static HostApplicationBuilder AddAtlasEdgeWindowsServiceFoundation(this HostApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(WindowsServiceOptions.SectionName);
        builder.Services.AddOptions<WindowsServiceOptions>()
            .Bind(section)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ServiceName),
                "WindowsService:ServiceName is required.")
            .Validate(
                options => options.HealthHeartbeatIntervalSeconds is >= 5 and <= 3600,
                "WindowsService:HealthHeartbeatIntervalSeconds must be between 5 and 3600.")
            .ValidateOnStart();

        var configured = section.Get<WindowsServiceOptions>() ?? new WindowsServiceOptions();
        builder.Services.AddWindowsService(options => options.ServiceName = configured.ServiceName);
        if (OperatingSystem.IsWindows())
        {
            ConfigureWindowsEventLog(builder.Services, configured.EventLogSourceName);
        }
        builder.Services.AddSingleton<WindowsServiceLifecycleState>();
        builder.Services.AddSingleton<WindowsServiceDiagnosticsProvider>();
        builder.Services.AddHostedService<WindowsServiceLifecycleHostedService>();
        builder.Services.AddHostedService<WindowsServiceHealthHeartbeatService>();

        return builder;
    }

    [SupportedOSPlatform("windows")]
    private static void ConfigureWindowsEventLog(IServiceCollection services, string sourceName) =>
        services.Configure<EventLogSettings>(settings =>
        {
            settings.LogName = "Application";
            settings.SourceName = sourceName;
        });
}
