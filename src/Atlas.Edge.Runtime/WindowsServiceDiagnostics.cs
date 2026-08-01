using System.Reflection;

namespace Atlas.Edge.Runtime;

public sealed record WindowsServiceDiagnostics(
    WindowsServiceLifecyclePhase RunningState,
    string Version,
    string BuildNumber,
    string InstallPath,
    TimeSpan RuntimeUptime,
    DateTimeOffset? LastServiceHeartbeatUtc,
    DateTimeOffset? LastDiscoveryUtc,
    DateTimeOffset? LastHealthUpdateUtc);

public sealed class WindowsServiceDiagnosticsProvider
{
    private readonly ScannerHealthState _healthState;
    private readonly ScannerInventoryState _inventoryState;
    private readonly WindowsServiceLifecycleState _lifecycleState;
    private readonly TimeProvider _timeProvider;

    public WindowsServiceDiagnosticsProvider(
        WindowsServiceLifecycleState lifecycleState,
        ScannerInventoryState inventoryState,
        ScannerHealthState healthState,
        TimeProvider timeProvider)
    {
        _lifecycleState = lifecycleState;
        _inventoryState = inventoryState;
        _healthState = healthState;
        _timeProvider = timeProvider;
    }

    public WindowsServiceDiagnostics Read()
    {
        var lifecycle = _lifecycleState.Current;
        var assembly = typeof(WindowsServiceDiagnosticsProvider).Assembly;
        var version = assembly.GetName().Version?.ToString() ?? "Unknown";
        var buildNumber = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            version;
        var uptime = _timeProvider.GetUtcNow() - lifecycle.ProcessStartedAtUtc;

        return new WindowsServiceDiagnostics(
            lifecycle.Phase,
            version,
            buildNumber,
            AppContext.BaseDirectory,
            uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime,
            lifecycle.LastHealthHeartbeatUtc,
            _inventoryState.Current?.DiscoveredAtUtc,
            _healthState.Current?.CapturedAtUtc);
    }
}
