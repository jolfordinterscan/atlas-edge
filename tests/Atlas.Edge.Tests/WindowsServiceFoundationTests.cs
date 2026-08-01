using System.Collections.Immutable;
using Atlas.Edge.Runtime;
using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.ScannerHealth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Tests;

public sealed class WindowsServiceFoundationTests
{
    [Fact]
    public void LifecycleState_TracksStartupRunningHeartbeatStoppingAndRestart()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        var state = new WindowsServiceLifecycleState(time);

        state.RecordStartup();
        time.Advance(TimeSpan.FromSeconds(1));
        state.RecordRunning();
        time.Advance(TimeSpan.FromSeconds(2));
        state.RecordHealthHeartbeat();

        Assert.Equal(WindowsServiceLifecyclePhase.Running, state.Current.Phase);
        Assert.Equal(time.GetUtcNow(), state.Current.LastHealthHeartbeatUtc);
        Assert.Equal(1, state.Current.StartupCount);
        Assert.Equal(0, state.Current.RestartCount);

        state.RecordStopping();
        Assert.Equal(WindowsServiceLifecyclePhase.Stopping, state.Current.Phase);
        state.RecordStopped();
        Assert.Equal(WindowsServiceLifecyclePhase.Stopped, state.Current.Phase);

        state.RecordStartup();
        Assert.Equal(WindowsServiceLifecyclePhase.Startup, state.Current.Phase);
        Assert.Equal(2, state.Current.StartupCount);
        Assert.Equal(1, state.Current.RestartCount);
    }

    [Fact]
    public void Diagnostics_ReportsVersionInstallPathUptimeAndScannerUpdateTimes()
    {
        var started = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(started);
        var lifecycle = new WindowsServiceLifecycleState(time);
        lifecycle.RecordStartup();
        lifecycle.RecordRunning();
        lifecycle.RecordHealthHeartbeat();
        var inventory = new ScannerInventoryState();
        var health = new ScannerHealthState();
        var discoveryAt = started.AddMinutes(1);
        var healthAt = started.AddMinutes(2);
        inventory.Update(new ScannerDiscoverySnapshot(
            discoveryAt,
            Array.Empty<DiscoveredScanner>(),
            Array.Empty<ScannerAdapterDiagnostic>()));
        health.Update(new ScannerHealthCollectionSnapshot(
            healthAt,
            ImmutableArray<ScannerHealthSnapshot>.Empty,
            ImmutableArray<ScannerHealthProviderDiagnostic>.Empty));
        time.Advance(TimeSpan.FromMinutes(5));
        var provider = new WindowsServiceDiagnosticsProvider(lifecycle, inventory, health, time);

        var diagnostics = provider.Read();

        Assert.Equal(WindowsServiceLifecyclePhase.Running, diagnostics.RunningState);
        Assert.NotEqual("Unknown", diagnostics.Version);
        Assert.NotEqual("Unknown", diagnostics.BuildNumber);
        Assert.Equal(AppContext.BaseDirectory, diagnostics.InstallPath);
        Assert.Equal(TimeSpan.FromMinutes(5), diagnostics.RuntimeUptime);
        Assert.Equal(started, diagnostics.LastServiceHeartbeatUtc);
        Assert.Equal(discoveryAt, diagnostics.LastDiscoveryUtc);
        Assert.Equal(healthAt, diagnostics.LastHealthUpdateUtc);
    }

    [Fact]
    public async Task RegisteredHost_StartsHeartbeatsAndStopsGracefully()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WindowsService:ServiceName"] = "Atlas Edge Test Service",
            ["WindowsService:EventLogSourceName"] = "Atlas Edge Test Service",
            ["WindowsService:HealthHeartbeatIntervalSeconds"] = "5"
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ScannerInventoryState>();
        builder.Services.AddSingleton<ScannerHealthState>();
        builder.AddAtlasEdgeWindowsServiceFoundation();

        using var host = builder.Build();
        await host.StartAsync();

        var state = host.Services.GetRequiredService<WindowsServiceLifecycleState>();
        Assert.Equal(WindowsServiceLifecyclePhase.Running, state.Current.Phase);
        Assert.NotNull(state.Current.LastHealthHeartbeatUtc);

        await host.StopAsync();
        Assert.Equal(WindowsServiceLifecyclePhase.Stopped, state.Current.Phase);
    }

    [Fact]
    public void Registration_BindsServiceEventLogAndInertLocalConfiguration()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WindowsService:ServiceName"] = "Atlas Edge Test Service",
            ["WindowsService:DisplayName"] = "Atlas Edge Test Display",
            ["WindowsService:EventLogSourceName"] = "Atlas Edge Test Source",
            ["WindowsService:HealthHeartbeatIntervalSeconds"] = "45",
            ["WindowsService:LocalConfiguration:Enabled"] = "true",
            ["WindowsService:LocalConfiguration:Path"] = "C:\\ProgramData\\Atlas Edge\\edge.json"
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ScannerInventoryState>();
        builder.Services.AddSingleton<ScannerHealthState>();
        builder.AddAtlasEdgeWindowsServiceFoundation();

        using var provider = builder.Services.BuildServiceProvider();
        var service = provider.GetRequiredService<IOptions<WindowsServiceOptions>>().Value;
        Assert.Equal("Atlas Edge Test Service", service.ServiceName);
        Assert.Equal("Atlas Edge Test Display", service.DisplayName);
        Assert.Equal(45, service.HealthHeartbeatIntervalSeconds);
        Assert.True(service.LocalConfiguration.Enabled);
        Assert.Equal("C:\\ProgramData\\Atlas Edge\\edge.json", service.LocalConfiguration.Path);
        if (OperatingSystem.IsWindows())
        {
            var eventLog = provider.GetRequiredService<IOptions<EventLogSettings>>().Value;
            Assert.Equal("Application", eventLog.LogName);
            Assert.Equal("Atlas Edge Test Source", eventLog.SourceName);
        }
        else
        {
            var registration = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "src",
                "Atlas.Edge.Runtime",
                "WindowsServiceRegistration.cs"));
            Assert.Contains("OperatingSystem.IsWindows()", registration, StringComparison.Ordinal);
            Assert.Contains("EventLogSettings", registration, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Registration_RejectsUnsafeHeartbeatInterval()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["WindowsService:HealthHeartbeatIntervalSeconds"] = "1";
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ScannerInventoryState>();
        builder.Services.AddSingleton<ScannerHealthState>();
        builder.AddAtlasEdgeWindowsServiceFoundation();

        using var host = builder.Build();
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public void Foundation_HasNoNetworkScannerCommandQueueOrPersistenceSurface()
    {
        var root = FindRepositoryRoot();
        var runtimeDirectory = Path.Combine(root, "src", "Atlas.Edge.Runtime");
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(runtimeDirectory, "WindowsService*.cs").Select(File.ReadAllText));
        var forbiddenTerms = new[]
        {
            "HttpClient",
            "HttpListener",
            "Socket",
            "IEventQueue",
            "IEventTransport",
            "ScannerCommand",
            "RemoteControl",
            "File.Write",
            "File.Read",
            "Registry",
            "Atlas.Edge.Knowledge"
        };

        Assert.All(forbiddenTerms, term => Assert.DoesNotContain(term, source, StringComparison.OrdinalIgnoreCase));
        var project = File.ReadAllText(Path.Combine(
            runtimeDirectory,
            "Atlas.Edge.Runtime.csproj"));
        Assert.Contains("Microsoft.Extensions.Hosting.WindowsServices", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Kestrel", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Atlas.Edge.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
