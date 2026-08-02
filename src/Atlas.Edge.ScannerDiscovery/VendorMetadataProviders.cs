using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Atlas.Edge.ScannerDiscovery;

public enum VendorSoftwareArchitecture
{
    Unknown,
    X86,
    X64,
    AnyCpu
}

public enum VendorInstallationSourceKind
{
    InstalledProgram,
    ProgramFiles,
    WindowsService,
    ComServer,
    TwainComponent,
    WiaProvider
}

public enum VendorInterfaceKind
{
    Unknown,
    Com,
    NativeLibrary,
    CommandLineCandidate,
    RegistryMetadata,
    NamedPipe,
    Configuration,
    Twain,
    Wia,
    DocumentedSdk
}

public sealed record VendorSdkCandidate(
    string Name,
    string Path,
    string? Version,
    VendorSoftwareArchitecture Architecture,
    VendorInterfaceKind InterfaceKind);

public sealed record VendorInstallation(
    string Vendor,
    string ProductName,
    string? Version,
    string InstallPath,
    VendorSoftwareArchitecture Architecture,
    VendorInstallationSourceKind Source,
    IReadOnlyList<VendorSdkCandidate> SdkCandidates);

public sealed record VendorInstallationDiagnostic(string ErrorCode, string Source);

public sealed record VendorInstallationSnapshot(
    bool IsAvailable,
    IReadOnlyList<VendorInstallation> Installations,
    IReadOnlyList<VendorInstallationDiagnostic> Diagnostics);

public interface IVendorInstallationSource
{
    Task<VendorInstallationSnapshot> DiscoverAsync(CancellationToken cancellationToken);
}

public interface IVendorInstallationCatalog
{
    Task<VendorInstallationSnapshot> DiscoverAsync(CancellationToken cancellationToken);
}

public sealed class VendorInstallationCatalog : IVendorInstallationCatalog
{
    private readonly IReadOnlyList<IVendorInstallationSource> _sources;

    public VendorInstallationCatalog(IEnumerable<IVendorInstallationSource> sources) =>
        _sources = sources.ToArray();

    public static VendorInstallationCatalog CreateWindowsDefault() =>
        new([new WindowsVendorInstallationSource()]);

    public async Task<VendorInstallationSnapshot> DiscoverAsync(CancellationToken cancellationToken)
    {
        var available = false;
        var installations = new List<VendorInstallation>();
        var diagnostics = new List<VendorInstallationDiagnostic>();

        foreach (var source in _sources)
        {
            try
            {
                var result = await source.DiscoverAsync(cancellationToken);
                available |= result.IsAvailable;
                installations.AddRange(result.Installations);
                diagnostics.AddRange(result.Diagnostics);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                diagnostics.Add(new VendorInstallationDiagnostic("vendor_source_failure", source.GetType().Name));
            }
        }

        var normalized = installations
            .GroupBy(
                value => $"{value.Vendor}|{value.ProductName}|{value.InstallPath}|{value.Source}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => Merge(group.ToArray()))
            .OrderBy(value => value.Vendor, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Source)
            .ToArray();

        return new VendorInstallationSnapshot(available, normalized, diagnostics.ToArray());
    }

    private static VendorInstallation Merge(IReadOnlyList<VendorInstallation> values)
    {
        var first = values[0];
        return first with
        {
            Version = values.Select(value => value.Version).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            SdkCandidates = values.SelectMany(value => value.SdkCandidates)
                .DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }
}

public sealed class WindowsVendorInstallationSource : IVendorInstallationSource
{
    private const int MaximumRecordsPerSource = 512;
    private const int MaximumSdkCandidatesPerInstallation = 128;
    private const int MaximumPathLength = 1024;
    private static readonly string[] CandidateExtensions = [".dll", ".exe", ".ocx", ".tlb"];
    private static readonly string[] SdkMarkers =
        ["sdk", "api", "scanner", "paperstream", "pfu", "ricoh", "fujitsu", "twain", "wia", "scansnap"];

    public Task<VendorInstallationSnapshot> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new VendorInstallationSnapshot(false, [], []));
        }

        return Task.FromResult(DiscoverWindows(cancellationToken));
    }

    [SupportedOSPlatform("windows")]
    private static VendorInstallationSnapshot DiscoverWindows(CancellationToken cancellationToken)
    {
        var installations = new List<VendorInstallation>();
        var diagnostics = new List<VendorInstallationDiagnostic>();

        CollectSafely(() => ReadInstalledPrograms(cancellationToken), installations, diagnostics, "installed_programs");
        CollectSafely(() => ReadProgramFiles(cancellationToken), installations, diagnostics, "program_files");
        CollectSafely(() => ReadServices(cancellationToken), installations, diagnostics, "windows_services");
        CollectSafely(() => ReadComServers(cancellationToken), installations, diagnostics, "com_servers");
        CollectSafely(() => ReadStillImageProviders(cancellationToken), installations, diagnostics, "wia_providers");
        CollectSafely(ReadTwainComponents, installations, diagnostics, "twain_components");

        return new VendorInstallationSnapshot(true, installations.ToArray(), diagnostics.ToArray());
    }

    private static void CollectSafely(
        Func<IReadOnlyList<VendorInstallation>> collect,
        ICollection<VendorInstallation> installations,
        ICollection<VendorInstallationDiagnostic> diagnostics,
        string source)
    {
        try
        {
            foreach (var installation in collect()) installations.Add(installation);
        }
        catch (Exception)
        {
            diagnostics.Add(new VendorInstallationDiagnostic("vendor_catalog_source_failure", source));
        }
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<VendorInstallation> ReadInstalledPrograms(CancellationToken cancellationToken)
    {
        var results = new List<VendorInstallation>();
        ReadUninstallRoot(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            VendorSoftwareArchitecture.X64,
            results,
            cancellationToken);
        ReadUninstallRoot(
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            VendorSoftwareArchitecture.X86,
            results,
            cancellationToken);
        return results;
    }

    [SupportedOSPlatform("windows")]
    private static void ReadUninstallRoot(
        string registryPath,
        VendorSoftwareArchitecture architecture,
        ICollection<VendorInstallation> results,
        CancellationToken cancellationToken)
    {
        using var root = Registry.LocalMachine.OpenSubKey(registryPath, writable: false);
        if (root is null) return;
        foreach (var keyName in root.GetSubKeyNames())
        {
            if (results.Count >= MaximumRecordsPerSource) break;
            cancellationToken.ThrowIfCancellationRequested();
            using var key = root.OpenSubKey(keyName, writable: false);
            var name = ReadString(key, "DisplayName");
            var publisher = ReadString(key, "Publisher");
            var location = ReadString(key, "InstallLocation");
            if (!TryIdentifyVendor($"{name}|{publisher}|{location}", out var vendor) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var installPath = SafeDirectory(location);
            results.Add(CreateInstallation(
                vendor,
                name,
                ReadString(key, "DisplayVersion"),
                installPath,
                architecture,
                VendorInstallationSourceKind.InstalledProgram));
        }
    }

    private static IReadOnlyList<VendorInstallation> ReadProgramFiles(CancellationToken cancellationToken)
    {
        var results = new List<VendorInstallation>();
        var roots = new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), VendorSoftwareArchitecture.X64),
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), VendorSoftwareArchitecture.X86)
        }.DistinctBy(value => value.Item1, StringComparer.OrdinalIgnoreCase);

        foreach (var (root, architecture) in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            foreach (var directory in EnumerateVendorDirectories(root, cancellationToken))
            {
                if (!TryIdentifyVendor(directory, out var vendor)) continue;
                results.Add(CreateInstallation(
                    vendor,
                    Path.GetFileName(directory),
                    null,
                    directory,
                    architecture,
                    VendorInstallationSourceKind.ProgramFiles));
            }
        }

        return results;
    }

    private static IEnumerable<string> EnumerateVendorDirectories(string root, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var parent in SafeDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > MaximumRecordsPerSource) yield break;
            if (HasReparsePoint(parent)) continue;

            if (TryIdentifyVendor(parent, out _)) yield return parent;
            foreach (var child in SafeDirectories(parent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++count > MaximumRecordsPerSource) yield break;
                if (!HasReparsePoint(child) && TryIdentifyVendor(child, out _)) yield return child;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<VendorInstallation> ReadServices(CancellationToken cancellationToken)
    {
        const string servicesRoot = @"SYSTEM\CurrentControlSet\Services";
        var results = new List<VendorInstallation>();
        using var root = Registry.LocalMachine.OpenSubKey(servicesRoot, writable: false);
        if (root is null) return results;
        foreach (var keyName in root.GetSubKeyNames())
        {
            if (results.Count >= MaximumRecordsPerSource) break;
            cancellationToken.ThrowIfCancellationRequested();
            using var key = root.OpenSubKey(keyName, writable: false);
            var displayName = ReadString(key, "DisplayName") ?? keyName;
            var imagePath = ReadString(key, "ImagePath");
            if (!TryIdentifyVendor($"{keyName}|{displayName}|{imagePath}", out var vendor)) continue;
            var executable = ExtractExecutablePath(imagePath);
            results.Add(CreateInstallation(
                vendor,
                displayName,
                FileVersion(executable),
                SafeDirectory(Path.GetDirectoryName(executable)),
                ArchitectureFromPath(executable),
                VendorInstallationSourceKind.WindowsService));
        }
        return results;
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<VendorInstallation> ReadComServers(CancellationToken cancellationToken)
    {
        var results = new List<VendorInstallation>();
        ReadComRoot(@"SOFTWARE\Classes\CLSID", VendorSoftwareArchitecture.X64, results, cancellationToken);
        ReadComRoot(@"SOFTWARE\WOW6432Node\Classes\CLSID", VendorSoftwareArchitecture.X86, results, cancellationToken);
        return results;
    }

    [SupportedOSPlatform("windows")]
    private static void ReadComRoot(
        string registryPath,
        VendorSoftwareArchitecture architecture,
        ICollection<VendorInstallation> results,
        CancellationToken cancellationToken)
    {
        using var root = Registry.LocalMachine.OpenSubKey(registryPath, writable: false);
        if (root is null) return;
        foreach (var keyName in root.GetSubKeyNames())
        {
            if (results.Count >= MaximumRecordsPerSource) break;
            cancellationToken.ThrowIfCancellationRequested();
            using var key = root.OpenSubKey(keyName, writable: false);
            using var server = key?.OpenSubKey("InprocServer32", writable: false);
            var name = key?.GetValue(null)?.ToString();
            var path = server?.GetValue(null)?.ToString();
            if (!TryIdentifyVendor($"{name}|{path}", out var vendor) || string.IsNullOrWhiteSpace(path)) continue;
            var candidate = CreateCandidate(path, architecture, VendorInterfaceKind.Com);
            results.Add(new VendorInstallation(
                vendor,
                Bound(name) ?? Path.GetFileNameWithoutExtension(path),
                FileVersion(path),
                SafeDirectory(Path.GetDirectoryName(path)),
                architecture,
                VendorInstallationSourceKind.ComServer,
                candidate is null ? [] : [candidate]));
        }
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<VendorInstallation> ReadStillImageProviders(CancellationToken cancellationToken)
    {
        const string stillImageRoot = @"SYSTEM\CurrentControlSet\Control\StillImage\Devices";
        var results = new List<VendorInstallation>();
        using var root = Registry.LocalMachine.OpenSubKey(stillImageRoot, writable: false);
        if (root is null) return results;
        foreach (var keyName in root.GetSubKeyNames())
        {
            if (results.Count >= MaximumRecordsPerSource) break;
            cancellationToken.ThrowIfCancellationRequested();
            using var key = root.OpenSubKey(keyName, writable: false);
            var name = ReadString(key, "FriendlyName", "DeviceName") ?? keyName;
            var driver = ReadString(key, "DriverName", "USDClass");
            var manufacturer = ReadString(key, "Manufacturer");
            if (!TryIdentifyVendor($"{name}|{driver}|{manufacturer}", out var vendor)) continue;
            var candidate = CreateCandidate(driver, ArchitectureFromPath(driver), VendorInterfaceKind.Wia);
            results.Add(new VendorInstallation(
                vendor,
                name,
                FileVersion(driver),
                SafeDirectory(Path.GetDirectoryName(driver)),
                ArchitectureFromPath(driver),
                VendorInstallationSourceKind.WiaProvider,
                candidate is null ? [] : [candidate]));
        }
        return results;
    }

    private static IReadOnlyList<VendorInstallation> ReadTwainComponents()
    {
        var results = new List<VendorInstallation>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var (path, architecture) in new[]
        {
            (Path.Combine(windows, "System32", "twaindsm.dll"), VendorSoftwareArchitecture.X64),
            (Path.Combine(windows, "SysWOW64", "twaindsm.dll"), VendorSoftwareArchitecture.X86)
        })
        {
            if (!File.Exists(path)) continue;
            var candidate = CreateCandidate(path, architecture, VendorInterfaceKind.Twain)!;
            results.Add(new VendorInstallation(
                "TWAIN",
                "TWAIN Data Source Manager",
                FileVersion(path),
                SafeDirectory(Path.GetDirectoryName(path)),
                architecture,
                VendorInstallationSourceKind.TwainComponent,
                [candidate]));
        }
        return results;
    }

    private static VendorInstallation CreateInstallation(
        string vendor,
        string productName,
        string? version,
        string installPath,
        VendorSoftwareArchitecture architecture,
        VendorInstallationSourceKind source) =>
        new(
            vendor,
            Bound(productName) ?? "Unknown",
            Bound(version),
            BoundPath(installPath),
            architecture,
            source,
            FindSdkCandidates(installPath, architecture));

    private static IReadOnlyList<VendorSdkCandidate> FindSdkCandidates(
        string installPath,
        VendorSoftwareArchitecture architecture)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath) || HasReparsePoint(installPath))
        {
            return [];
        }

        var candidates = new List<VendorSdkCandidate>();
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((installPath, 0));
        while (pending.Count > 0 && candidates.Count < MaximumSdkCandidatesPerInstallation)
        {
            var (directory, depth) = pending.Dequeue();
            foreach (var file in SafeFiles(directory))
            {
                if (!CandidateExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) ||
                    !SdkMarkers.Any(marker => Path.GetFileName(file).Contains(marker, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var candidate = CreateCandidate(file, architecture, InferInterface(file));
                if (candidate is not null) candidates.Add(candidate);
                if (candidates.Count >= MaximumSdkCandidatesPerInstallation) break;
            }

            if (depth >= 2) continue;
            foreach (var child in SafeDirectories(directory).Where(path => !HasReparsePoint(path)))
            {
                pending.Enqueue((child, depth + 1));
            }
        }

        return candidates.DistinctBy(value => value.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static VendorSdkCandidate? CreateCandidate(
        string? path,
        VendorSoftwareArchitecture architecture,
        VendorInterfaceKind interfaceKind)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumPathLength) return null;
        return new VendorSdkCandidate(
            Bound(Path.GetFileName(path)) ?? "Unknown",
            BoundPath(path),
            FileVersion(path),
            architecture,
            interfaceKind);
    }

    private static VendorInterfaceKind InferInterface(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ocx" or ".tlb" => VendorInterfaceKind.Com,
        ".dll" when path.Contains("twain", StringComparison.OrdinalIgnoreCase) => VendorInterfaceKind.Twain,
        ".dll" when path.Contains("wia", StringComparison.OrdinalIgnoreCase) => VendorInterfaceKind.Wia,
        ".dll" => VendorInterfaceKind.NativeLibrary,
        ".exe" => VendorInterfaceKind.CommandLineCandidate,
        _ => VendorInterfaceKind.Unknown
    };

    private static bool TryIdentifyVendor(string? text, out string vendor)
    {
        vendor = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Contains("PaperStream", StringComparison.OrdinalIgnoreCase)) vendor = "PaperStream";
        else if (text.Contains("RICOH", StringComparison.OrdinalIgnoreCase)) vendor = "Ricoh";
        else if (text.Contains("PFU", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("FUJITSU", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("ScanSnap", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("fi Series", StringComparison.OrdinalIgnoreCase)) vendor = "PFU";
        return vendor.Length > 0;
    }

    private static VendorSoftwareArchitecture ArchitectureFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return VendorSoftwareArchitecture.Unknown;
        if (path.Contains("Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("SysWOW64", StringComparison.OrdinalIgnoreCase)) return VendorSoftwareArchitecture.X86;
        if (path.Contains("Program Files", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("System32", StringComparison.OrdinalIgnoreCase)) return VendorSoftwareArchitecture.X64;
        return VendorSoftwareArchitecture.Unknown;
    }

    private static string ExtractExecutablePath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return string.Empty;
        var value = Environment.ExpandEnvironmentVariables(commandLine.Trim());
        if (value.StartsWith('"'))
        {
            var closing = value.IndexOf('"', 1);
            return closing > 1 ? value[1..closing] : string.Empty;
        }
        var executable = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return executable >= 0 ? value[..(executable + 4)] : string.Empty;
    }

    private static string? FileVersion(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) || !File.Exists(path)
                ? null
                : Bound(FileVersionInfo.GetVersionInfo(path).FileVersion);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path).Take(MaximumRecordsPerSource).ToArray(); }
        catch (Exception) { return []; }
    }

    private static IEnumerable<string> SafeFiles(string path)
    {
        try { return Directory.EnumerateFiles(path).Take(MaximumRecordsPerSource).ToArray(); }
        catch (Exception) { return []; }
    }

    private static bool HasReparsePoint(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception) { return true; }
    }

    private static string SafeDirectory(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > MaximumPathLength ? "Unknown" : value.Trim();

    private static string BoundPath(string value) =>
        value.Length > MaximumPathLength ? value[..MaximumPathLength] : value;

    private static string? Bound(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 256)];

    [SupportedOSPlatform("windows")]
    private static string? ReadString(RegistryKey? key, params string[] names) =>
        key is null
            ? null
            : names.Select(name => key.GetValue(name)?.ToString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public enum VendorMetadataAvailability
{
    Available,
    Unavailable,
    Unsupported,
    Unknown
}

public enum VendorMetadataField
{
    SerialNumber,
    FirmwareVersion,
    LifetimePageCount,
    RollerCount,
    Consumables,
    DeviceHealth,
    ErrorState,
    MaintenanceCounters
}

public sealed record VendorMetadataCapability(
    VendorMetadataField Field,
    VendorMetadataAvailability Availability,
    string ReasonCode);

public sealed record VendorMetadataProviderStatus(
    string ProviderName,
    string Vendor,
    bool IsInstalled,
    VendorMetadataAvailability Availability,
    IReadOnlyList<VendorMetadataCapability> Capabilities);

public interface IVendorMetadataProvider
{
    string ProviderName { get; }

    string Vendor { get; }

    VendorMetadataProviderStatus Detect(VendorInstallationSnapshot installations);
}

public interface IVendorScannerMetadataProvider : IVendorMetadataProvider
{
}

public abstract class StubVendorMetadataProvider : IVendorScannerMetadataProvider
{
    public abstract string ProviderName { get; }

    public abstract string Vendor { get; }

    protected abstract bool Matches(VendorInstallation installation);

    public VendorMetadataProviderStatus Detect(VendorInstallationSnapshot installations)
    {
        var installed = installations.Installations.Any(Matches);
        var state = installed ? VendorMetadataAvailability.Available : VendorMetadataAvailability.Unavailable;
        var capabilityState = installed ? VendorMetadataAvailability.Unsupported : VendorMetadataAvailability.Unavailable;
        var reason = installed ? "vendor_adapter_not_implemented" : "vendor_software_not_detected";
        return new VendorMetadataProviderStatus(
            ProviderName,
            Vendor,
            installed,
            state,
            Enum.GetValues<VendorMetadataField>()
                .Select(field => new VendorMetadataCapability(field, capabilityState, reason))
                .ToArray());
    }
}

public sealed class PaperStreamMetadataProvider : StubVendorMetadataProvider
{
    public override string ProviderName => "PaperStream";
    public override string Vendor => "Ricoh/PFU";
    protected override bool Matches(VendorInstallation installation) =>
        installation.Vendor.Equals("PaperStream", StringComparison.OrdinalIgnoreCase) ||
        installation.ProductName.Contains("PaperStream", StringComparison.OrdinalIgnoreCase);
}

public sealed class RicohMetadataProvider : StubVendorMetadataProvider
{
    public override string ProviderName => "Ricoh";
    public override string Vendor => "Ricoh";
    protected override bool Matches(VendorInstallation installation) =>
        installation.Vendor.Equals("Ricoh", StringComparison.OrdinalIgnoreCase);
}

public sealed class PFUMetadataProvider : StubVendorMetadataProvider
{
    public override string ProviderName => "PFU";
    public override string Vendor => "PFU";
    protected override bool Matches(VendorInstallation installation) =>
        installation.Vendor.Equals("PFU", StringComparison.OrdinalIgnoreCase);
}

public sealed class NoOpVendorMetadataProvider : IVendorMetadataProvider
{
    public string ProviderName => "NoOp";
    public string Vendor => "None";

    public VendorMetadataProviderStatus Detect(VendorInstallationSnapshot installations) =>
        new(
            ProviderName,
            Vendor,
            false,
            VendorMetadataAvailability.Unsupported,
            Enum.GetValues<VendorMetadataField>()
                .Select(field => new VendorMetadataCapability(
                    field,
                    VendorMetadataAvailability.Unsupported,
                    "vendor_metadata_disabled"))
                .ToArray());
}

public static class VendorMetadataProviderFactory
{
    public static IReadOnlyList<IVendorMetadataProvider> CreateDetectionProviders() =>
        [new PaperStreamMetadataProvider(), new RicohMetadataProvider(), new PFUMetadataProvider()];

    public static IVendorMetadataProvider CreateNoOp() => new NoOpVendorMetadataProvider();
}
