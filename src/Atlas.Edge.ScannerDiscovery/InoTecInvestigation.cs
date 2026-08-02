using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Atlas.Edge.ScannerDiscovery;

public enum InoTecInterfaceKind
{
    WindowsPnp,
    WiaSource,
    TwainSource,
    IsisDriver,
    ComRegistration,
    ComTypeLibrary,
    InstalledProgram,
    WindowsService,
    DriverFile,
    NativeLibrary,
    Executable,
    RegistryKey,
    ConfigurationFile,
    StatusFile,
    CounterFile,
    DiagnosticFile
}

public enum InoTecMetadataKind
{
    SerialNumber,
    FirmwareVersion,
    LifetimePageCount,
    ConsumablesOrRollers,
    ScannerHealth,
    ErrorState,
    MaintenanceCounters
}

public enum InoTecOpportunityRating
{
    Promising,
    Possible
}

public sealed record InoTecMetadataOpportunity(
    InoTecMetadataKind Metadata,
    InoTecOpportunityRating Rating,
    string ReasonCode);

public sealed record InoTecInterfaceEvidence(
    InoTecInterfaceKind Kind,
    string Name,
    string? Version,
    string? Location,
    VendorSoftwareArchitecture Architecture,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<string> ExportedFunctions,
    IReadOnlyList<InoTecMetadataOpportunity> Opportunities);

public sealed record InoTecInvestigationDiagnostic(string Source, string ErrorCode);

public sealed record InoTecInvestigationSnapshot(
    string SchemaVersion,
    DateTimeOffset CollectedAtUtc,
    bool IsAvailable,
    IReadOnlyList<InoTecInterfaceEvidence> Interfaces,
    IReadOnlyList<InoTecInvestigationDiagnostic> Diagnostics);

public sealed record InoTecInvestigationSourceResult(
    bool IsAvailable,
    IReadOnlyList<InoTecInterfaceEvidence> Interfaces,
    IReadOnlyList<InoTecInvestigationDiagnostic> Diagnostics);

public interface IInoTecInvestigationSource
{
    string SourceName { get; }

    Task<InoTecInvestigationSourceResult> InspectAsync(CancellationToken cancellationToken);
}

public sealed class InoTecInvestigator
{
    public const string SchemaVersion = "1.0";
    private readonly IReadOnlyList<IInoTecInvestigationSource> _sources;
    private readonly TimeProvider _timeProvider;

    public InoTecInvestigator(IEnumerable<IInoTecInvestigationSource> sources, TimeProvider timeProvider)
    {
        _sources = sources.ToArray();
        _timeProvider = timeProvider;
    }

    public static InoTecInvestigator CreateWindowsDefault() =>
        new([new WindowsInoTecInvestigationSource()], TimeProvider.System);

    public async Task<InoTecInvestigationSnapshot> InspectAsync(CancellationToken cancellationToken)
    {
        var available = false;
        var interfaces = new List<InoTecInterfaceEvidence>();
        var diagnostics = new List<InoTecInvestigationDiagnostic>();

        foreach (var source in _sources)
        {
            try
            {
                var result = await source.InspectAsync(cancellationToken);
                available |= result.IsAvailable;
                interfaces.AddRange(result.Interfaces);
                diagnostics.AddRange(result.Diagnostics);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                diagnostics.Add(new InoTecInvestigationDiagnostic(source.SourceName, "inotec_source_failure"));
            }
        }

        var normalized = interfaces
            .GroupBy(
                value => $"{value.Kind}|{value.Name}|{value.Location}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(value => value.Kind)
            .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new InoTecInvestigationSnapshot(
            SchemaVersion,
            _timeProvider.GetUtcNow(),
            available,
            normalized,
            diagnostics.ToArray());
    }
}

public static class InoTecEvidenceClassifier
{
    private static readonly IReadOnlyDictionary<InoTecMetadataKind, string[]> Tokens =
        new Dictionary<InoTecMetadataKind, string[]>
        {
            [InoTecMetadataKind.SerialNumber] = ["serial", "seriennummer", "deviceid"],
            [InoTecMetadataKind.FirmwareVersion] = ["firmware", "fwversion", "revision"],
            [InoTecMetadataKind.LifetimePageCount] = ["lifetime", "pagecount", "page_count", "sheetcount", "totalcount"],
            [InoTecMetadataKind.ConsumablesOrRollers] = ["consumable", "roller", "pad", "wear"],
            [InoTecMetadataKind.ScannerHealth] = ["health", "ready", "device_status", "devicestatus"],
            [InoTecMetadataKind.ErrorState] = ["error", "fault", "alarm", "diagnostic"],
            [InoTecMetadataKind.MaintenanceCounters] = ["maintenance", "servicecounter", "cleaning", "maintcount"]
        };

    public static bool IsInoTec(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Contains("InoTec", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("Datawin", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("SCAMAX", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<InoTecMetadataOpportunity> Classify(
        InoTecInterfaceKind kind,
        params string?[] evidence)
    {
        var text = string.Join('|', evidence.Where(value => !string.IsNullOrWhiteSpace(value)));
        var opportunities = new List<InoTecMetadataOpportunity>();
        foreach (var pair in Tokens)
        {
            if (pair.Value.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                opportunities.Add(new InoTecMetadataOpportunity(
                    pair.Key,
                    InoTecOpportunityRating.Promising,
                    "metadata_name_match"));
            }
        }

        foreach (var metadata in BaselinePossibilities(kind))
        {
            if (opportunities.All(value => value.Metadata != metadata))
            {
                opportunities.Add(new InoTecMetadataOpportunity(
                    metadata,
                    InoTecOpportunityRating.Possible,
                    "interface_requires_documentation"));
            }
        }

        return opportunities.OrderBy(value => value.Metadata).ToArray();
    }

    private static IEnumerable<InoTecMetadataKind> BaselinePossibilities(InoTecInterfaceKind kind) => kind switch
    {
        InoTecInterfaceKind.WindowsPnp or InoTecInterfaceKind.WiaSource or
            InoTecInterfaceKind.TwainSource or InoTecInterfaceKind.IsisDriver =>
            [InoTecMetadataKind.SerialNumber, InoTecMetadataKind.FirmwareVersion, InoTecMetadataKind.ErrorState],
        InoTecInterfaceKind.ComRegistration or InoTecInterfaceKind.ComTypeLibrary or
            InoTecInterfaceKind.NativeLibrary => Enum.GetValues<InoTecMetadataKind>(),
        InoTecInterfaceKind.ConfigurationFile or InoTecInterfaceKind.StatusFile or
            InoTecInterfaceKind.CounterFile or InoTecInterfaceKind.DiagnosticFile or
            InoTecInterfaceKind.RegistryKey => Enum.GetValues<InoTecMetadataKind>(),
        _ => []
    };
}

public sealed class WindowsInoTecInvestigationSource : IInoTecInvestigationSource
{
    private const int MaximumRecords = 2048;
    private const int MaximumFiles = 1024;
    private static readonly string[] BinaryExtensions = [".dll", ".ocx", ".exe", ".ds"];
    private static readonly string[] MetadataExtensions = [".config", ".json", ".xml", ".ini", ".log", ".db", ".sqlite", ".dat", ".status"];
    private static readonly string[] RegistryRoots = [@"SOFTWARE\InoTec", @"SOFTWARE\Datawin", @"SOFTWARE\SCAMAX"];

    public string SourceName => "WindowsInoTec";

    public async Task<InoTecInvestigationSourceResult> InspectAsync(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new(false, [], []);

        return await InspectWindowsAsync(cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static async Task<InoTecInvestigationSourceResult> InspectWindowsAsync(
        CancellationToken cancellationToken)
    {
        var interfaces = new List<InoTecInterfaceEvidence>();
        var diagnostics = new List<InoTecInvestigationDiagnostic>();
        await CollectAsync(() => AddWiaAsync(interfaces, cancellationToken), diagnostics, "wia");
        await CollectAsync(() => AddPnpAsync(interfaces, cancellationToken), diagnostics, "pnp");
        await CollectAsync(() => AddStandardSourcesAsync(interfaces, cancellationToken), diagnostics, "twain_isis");

        VendorInstallation[] inotecInstallations = [];
        await CollectAsync(async () =>
        {
            var vendor = await VendorInstallationCatalog.CreateWindowsDefault().DiscoverAsync(cancellationToken);
            inotecInstallations = vendor.Installations
                .Where(value => InoTecEvidenceClassifier.IsInoTec(
                    $"{value.Vendor}|{value.ProductName}|{value.InstallPath}"))
                .ToArray();
            AddVendorInstallations(interfaces, inotecInstallations);
        }, diagnostics, "installed_software");
        Collect(() => AddRegistryEvidence(interfaces, cancellationToken), diagnostics, "registry");
        Collect(() => AddComEvidence(interfaces, cancellationToken), diagnostics, "com");
        Collect(() => AddTypeLibraryEvidence(interfaces, cancellationToken), diagnostics, "typelib");
        Collect(() => AddFiles(interfaces, inotecInstallations, cancellationToken), diagnostics, "files");
        return new InoTecInvestigationSourceResult(true, interfaces.ToArray(), diagnostics.ToArray());
    }

    private static async Task CollectAsync(
        Func<Task> collect,
        ICollection<InoTecInvestigationDiagnostic> diagnostics,
        string source)
    {
        try
        {
            await collect();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            diagnostics.Add(new InoTecInvestigationDiagnostic(source, "inotec_surface_unavailable"));
        }
    }

    private static void Collect(
        Action collect,
        ICollection<InoTecInvestigationDiagnostic> diagnostics,
        string source)
    {
        try
        {
            collect();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            diagnostics.Add(new InoTecInvestigationDiagnostic(source, "inotec_surface_unavailable"));
        }
    }

    private static async Task AddWiaAsync(
        ICollection<InoTecInterfaceEvidence> output,
        CancellationToken cancellationToken)
    {
        var result = await new WiaScannerSourceCatalog().EnumerateAsync(cancellationToken);
        foreach (var source in result.Sources.Where(IsInoTecSource))
        {
            var properties = new Dictionary<string, string>
            {
                ["Manufacturer"] = source.Manufacturer,
                ["Model"] = source.Model,
                ["SourceIdHash"] = InoTecInvestigationPrivacy.HashIdentifier(source.SourceId),
                ["DevicePathHash"] = InoTecInvestigationPrivacy.HashOrUnknown(source.DevicePath),
                ["DriverName"] = source.Driver.Name,
                ["DriverVersion"] = Value(source.Driver.Version)
            };
            output.Add(Create(
                InoTecInterfaceKind.WiaSource,
                source.Model,
                source.Driver.Version,
                null,
                VendorSoftwareArchitecture.Unknown,
                properties,
                []));
        }
    }

    private static async Task AddPnpAsync(
        ICollection<InoTecInterfaceEvidence> output,
        CancellationToken cancellationToken)
    {
        var result = await new WindowsPnpScannerMetadataCatalog().ReadAsync(cancellationToken);
        foreach (var record in result.Records.Where(record => InoTecEvidenceClassifier.IsInoTec(
                     $"{record.Manufacturer}|{record.FriendlyName}|{record.DriverName}|{record.DriverProvider}")))
        {
            var normalized = WindowsPnpScannerMetadataProvider.Normalize(record);
            var properties = new Dictionary<string, string>
            {
                ["Manufacturer"] = Value(record.Manufacturer),
                ["FriendlyName"] = Value(record.FriendlyName),
                ["Service"] = Value(record.Service),
                ["DriverName"] = Value(record.DriverName),
                ["DriverProvider"] = Value(record.DriverProvider),
                ["DriverVersion"] = Value(record.DriverVersion),
                ["UsbVendorId"] = Value(normalized.UsbVendorId),
                ["UsbProductId"] = Value(normalized.UsbProductId),
                ["DeviceInstanceIdHash"] = InoTecInvestigationPrivacy.HashIdentifier(record.DeviceInstanceId),
                ["HardwareIdHashes"] = string.Join(',', record.HardwareIds.Select(InoTecInvestigationPrivacy.HashIdentifier)),
                ["ContainerIdHash"] = InoTecInvestigationPrivacy.HashOrUnknown(record.ContainerId),
                ["LocationPathHashes"] = string.Join(',', record.LocationPaths.Select(InoTecInvestigationPrivacy.HashIdentifier)),
                ["SerialMasked"] = ScannerMetadataPrivacy.MaskSerial(normalized.SerialNumber)
            };
            output.Add(Create(
                InoTecInterfaceKind.WindowsPnp,
                Value(record.FriendlyName),
                record.DriverVersion,
                null,
                VendorSoftwareArchitecture.Unknown,
                properties,
                []));
        }
    }

    private static async Task AddStandardSourcesAsync(
        ICollection<InoTecInterfaceEvidence> output,
        CancellationToken cancellationToken)
    {
        var twain = await new TwainScannerSourceCatalog().EnumerateAsync(cancellationToken);
        AddSources(output, twain.Sources, InoTecInterfaceKind.TwainSource);
        var isis = await new IsisScannerSourceCatalog().EnumerateAsync(cancellationToken);
        AddSources(output, isis.Sources, InoTecInterfaceKind.IsisDriver);
    }

    private static void AddSources(
        ICollection<InoTecInterfaceEvidence> output,
        IEnumerable<ScannerSourceMetadata> sources,
        InoTecInterfaceKind kind)
    {
        foreach (var source in sources.Where(IsInoTecSource))
        {
            var properties = new Dictionary<string, string>
            {
                ["Manufacturer"] = source.Manufacturer,
                ["Model"] = source.Model,
                ["SourceIdHash"] = InoTecInvestigationPrivacy.HashIdentifier(source.SourceId),
                ["DriverName"] = source.Driver.Name,
                ["DriverVersion"] = Value(source.Driver.Version),
                ["MetadataKeys"] = string.Join(',', (source.Metadata?.Keys ?? []).Order())
            };
            output.Add(Create(
                kind,
                source.Model,
                source.Driver.Version,
                SafeInstalledPath(source.SourceId),
                VendorSoftwareArchitecture.Unknown,
                properties,
                []));
        }
    }

    private static void AddVendorInstallations(
        ICollection<InoTecInterfaceEvidence> output,
        IEnumerable<VendorInstallation> installations)
    {
        foreach (var installation in installations)
        {
            var kind = installation.Source switch
            {
                VendorInstallationSourceKind.WindowsService => InoTecInterfaceKind.WindowsService,
                VendorInstallationSourceKind.ComServer => InoTecInterfaceKind.ComRegistration,
                VendorInstallationSourceKind.WiaProvider => InoTecInterfaceKind.DriverFile,
                VendorInstallationSourceKind.TwainComponent => InoTecInterfaceKind.TwainSource,
                _ => InoTecInterfaceKind.InstalledProgram
            };
            output.Add(Create(
                kind,
                installation.ProductName,
                installation.Version,
                SafeInstalledPath(installation.InstallPath),
                installation.Architecture,
                new Dictionary<string, string>
                {
                    ["Vendor"] = installation.Vendor,
                    ["DiscoverySource"] = installation.Source.ToString(),
                    ["SdkCandidateNames"] = string.Join(',', installation.SdkCandidates.Select(value => value.Name))
                },
                []));
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistryEvidence(
        ICollection<InoTecInterfaceEvidence> output,
        CancellationToken cancellationToken)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var rootPath in RegistryRoots)
                {
                    using var root = baseKey.OpenSubKey(rootPath, writable: false);
                    if (root is null) continue;
                    AddRegistryTree(output, root, $"{hive}:{view}:{rootPath}", 0, cancellationToken);
                }
            }
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistryTree(
        ICollection<InoTecInterfaceEvidence> output,
        RegistryKey key,
        string path,
        int depth,
        CancellationToken cancellationToken)
    {
        if (output.Count >= MaximumRecords || depth > 4) return;
        cancellationToken.ThrowIfCancellationRequested();
        var valueNames = key.GetValueNames().Where(value => !string.IsNullOrWhiteSpace(value)).Order().ToArray();
        output.Add(Create(
            InoTecInterfaceKind.RegistryKey,
            "InoTec/Datawin registry key",
            null,
            $"registry-sha256:{InoTecInvestigationPrivacy.HashIdentifier(path)}",
            VendorSoftwareArchitecture.Unknown,
            new Dictionary<string, string> { ["ValueNames"] = string.Join(',', valueNames) },
            []));
        foreach (var childName in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(childName, writable: false);
            if (child is not null) AddRegistryTree(output, child, $"{path}\\{childName}", depth + 1, cancellationToken);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddComEvidence(
        ICollection<InoTecInterfaceEvidence> output,
        CancellationToken cancellationToken)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var root = baseKey.OpenSubKey(@"SOFTWARE\Classes\CLSID", writable: false);
                if (root is null) continue;
                foreach (var clsid in root.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var key = root.OpenSubKey(clsid, writable: false);
                    using var inProcessServer = key?.OpenSubKey("InprocServer32", writable: false);
                    using var localServer = key?.OpenSubKey("LocalServer32", writable: false);
                    using var progId = key?.OpenSubKey("ProgID", writable: false);
                    var name = key?.GetValue(null)?.ToString();
                    var rawServer = inProcessServer?.GetValue(null)?.ToString() ?? localServer?.GetValue(null)?.ToString();
                    var path = ExtractComServerPath(rawServer);
                    var programmaticId = progId?.GetValue(null)?.ToString();
                    if (!InoTecEvidenceClassifier.IsInoTec($"{name}|{path}|{programmaticId}")) continue;
                    output.Add(Create(
                        InoTecInterfaceKind.ComRegistration,
                        Value(name ?? programmaticId),
                        FileVersion(path),
                        SafeInstalledPath(path),
                        view == RegistryView.Registry64 ? VendorSoftwareArchitecture.X64 : VendorSoftwareArchitecture.X86,
                        new Dictionary<string, string>
                        {
                            ["ClassIdHash"] = InoTecInvestigationPrivacy.HashIdentifier(clsid),
                            ["ProgrammaticId"] = Value(programmaticId),
                            ["RegistryScope"] = hive.ToString(),
                            ["ServerKind"] = inProcessServer is null ? "LocalServer" : "InProcess",
                            ["CallableRegistration"] = File.Exists(path).ToString()
                        },
                        []));
                }
            }
    }

    [SupportedOSPlatform("windows")]
    private static void AddTypeLibraryEvidence(
        ICollection<InoTecInterfaceEvidence> output,
        CancellationToken cancellationToken)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var root = baseKey.OpenSubKey(@"SOFTWARE\Classes\TypeLib", writable: false);
                if (root is null) continue;
                foreach (var typeLibId in root.GetSubKeyNames())
                    foreach (var versionName in OpenNames(root, typeLibId))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var version = root.OpenSubKey($@"{typeLibId}\{versionName}", writable: false);
                        var name = version?.GetValue(null)?.ToString();
                        var path = ReadTypeLibraryPath(version, view);
                        if (!InoTecEvidenceClassifier.IsInoTec($"{name}|{path}")) continue;
                        output.Add(Create(
                            InoTecInterfaceKind.ComTypeLibrary,
                            Value(name),
                            versionName,
                            SafeInstalledPath(path),
                            view == RegistryView.Registry64 ? VendorSoftwareArchitecture.X64 : VendorSoftwareArchitecture.X86,
                            new Dictionary<string, string>
                            {
                                ["TypeLibraryIdHash"] = InoTecInvestigationPrivacy.HashIdentifier(typeLibId),
                                ["RegistryScope"] = hive.ToString(),
                                ["RegisteredFileExists"] = File.Exists(path).ToString(),
                                ["LoadedOrInstantiated"] = "False"
                            },
                            []));
                    }
            }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> OpenNames(RegistryKey root, string childName)
    {
        try
        {
            using var child = root.OpenSubKey(childName, writable: false);
            return child?.GetSubKeyNames() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadTypeLibraryPath(RegistryKey? version, RegistryView view)
    {
        var architecture = view == RegistryView.Registry64 ? "win64" : "win32";
        using var key = version?.OpenSubKey($@"0\{architecture}", writable: false);
        return key?.GetValue(null)?.ToString();
    }

    private static string? ExtractComServerPath(string? registration)
    {
        if (string.IsNullOrWhiteSpace(registration)) return null;
        var value = Environment.ExpandEnvironmentVariables(registration.Trim());
        if (value.StartsWith('"'))
        {
            var closingQuote = value.IndexOf('"', 1);
            return closingQuote > 1 ? value[1..closingQuote] : null;
        }

        foreach (var extension in new[] { ".exe", ".dll", ".ocx" })
        {
            var end = value.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (end >= 0) return value[..(end + extension.Length)];
        }

        return value;
    }

    private static void AddFiles(
        ICollection<InoTecInterfaceEvidence> output,
        IEnumerable<VendorInstallation> installations,
        CancellationToken cancellationToken)
    {
        var roots = installations.Select(value => value.InstallPath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && path != "Unknown" && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var fileCount = 0;
        foreach (var root in roots)
        {
            var pending = new Queue<(string Path, int Depth)>();
            pending.Enqueue((root, 0));
            while (pending.Count > 0 && fileCount < MaximumFiles)
            {
                var (directory, depth) = pending.Dequeue();
                cancellationToken.ThrowIfCancellationRequested();
                if (IsReparsePoint(directory)) continue;
                foreach (var file in SafeFiles(directory))
                {
                    if (++fileCount > MaximumFiles) break;
                    var extension = Path.GetExtension(file);
                    if (BinaryExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        AddBinary(output, file);
                    }
                    else if (MetadataExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        AddMetadataFile(output, file);
                    }
                }

                if (depth >= 4) continue;
                foreach (var child in SafeDirectories(directory)) pending.Enqueue((child, depth + 1));
            }
        }
    }

    private static void AddBinary(ICollection<InoTecInterfaceEvidence> output, string path)
    {
        var extension = Path.GetExtension(path);
        var kind = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            ? InoTecInterfaceKind.Executable
            : extension.Equals(".ds", StringComparison.OrdinalIgnoreCase)
                ? InoTecInterfaceKind.DriverFile
                : InoTecInterfaceKind.NativeLibrary;
        var exports = extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                      extension.Equals(".ocx", StringComparison.OrdinalIgnoreCase) ||
                      extension.Equals(".ds", StringComparison.OrdinalIgnoreCase)
            ? PortableExecutableExportReader.ReadExportNames(path)
            : [];
        output.Add(Create(
            kind,
            Path.GetFileName(path),
            FileVersion(path),
            SafeInstalledPath(path),
            ArchitectureFromPath(path),
            new Dictionary<string, string>
            {
                ["FileSizeBytes"] = SafeFileLength(path).ToString(),
                ["ExportInspection"] = "StaticOnly",
                ["LoadedOrExecuted"] = "False"
            },
            exports));
    }

    private static void AddMetadataFile(ICollection<InoTecInterfaceEvidence> output, string path)
    {
        var name = Path.GetFileName(path);
        var kind = name.Contains("counter", StringComparison.OrdinalIgnoreCase)
            ? InoTecInterfaceKind.CounterFile
            : name.Contains("status", StringComparison.OrdinalIgnoreCase)
                ? InoTecInterfaceKind.StatusFile
                : name.Contains("log", StringComparison.OrdinalIgnoreCase) || name.Contains("diag", StringComparison.OrdinalIgnoreCase)
                    ? InoTecInterfaceKind.DiagnosticFile
                    : InoTecInterfaceKind.ConfigurationFile;
        output.Add(Create(
            kind,
            name,
            null,
            SafeInstalledPath(path),
            VendorSoftwareArchitecture.Unknown,
            new Dictionary<string, string>
            {
                ["FileSizeBytes"] = SafeFileLength(path).ToString(),
                ["LastModifiedUtc"] = SafeLastWrite(path)?.ToString("O") ?? "Unknown",
                ["ContentsRead"] = "False"
            },
            []));
    }

    private static InoTecInterfaceEvidence Create(
        InoTecInterfaceKind kind,
        string name,
        string? version,
        string? location,
        VendorSoftwareArchitecture architecture,
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyList<string> exports)
    {
        var evidence = new[]
        {
            name,
            location,
            string.Join('|', properties.Keys),
            string.Join('|', exports)
        };
        return new InoTecInterfaceEvidence(
            kind,
            Bound(name),
            BoundNullable(version),
            location,
            architecture,
            properties.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            exports.Take(512).Select(Bound).ToArray(),
            InoTecEvidenceClassifier.Classify(kind, evidence));
    }

    private static bool IsInoTecSource(ScannerSourceMetadata source) =>
        InoTecEvidenceClassifier.IsInoTec(
            $"{source.Manufacturer}|{source.Model}|{source.Driver.Name}|{source.Driver.Provider}");

    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : Bound(value);

    private static string? SafeInstalledPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024) return null;
        try
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            var allowedRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            }.Where(value => !string.IsNullOrWhiteSpace(value));
            return allowedRoots.Any(root => full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                ? full
                : $"sha256:{InoTecInvestigationPrivacy.HashIdentifier(full)}";
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? FileVersion(string? path)
    {
        try { return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : FileVersionInfo.GetVersionInfo(path).FileVersion; }
        catch (Exception) { return null; }
    }

    private static VendorSoftwareArchitecture ArchitectureFromPath(string path) =>
        path.Contains("Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("SysWOW64", StringComparison.OrdinalIgnoreCase)
            ? VendorSoftwareArchitecture.X86
            : path.Contains("Program Files", StringComparison.OrdinalIgnoreCase) ||
              path.Contains("System32", StringComparison.OrdinalIgnoreCase)
                ? VendorSoftwareArchitecture.X64
                : VendorSoftwareArchitecture.Unknown;

    private static IEnumerable<string> SafeFiles(string path)
    {
        try { return Directory.EnumerateFiles(path).Take(MaximumFiles).ToArray(); }
        catch (Exception) { return []; }
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path).Take(MaximumFiles).ToArray(); }
        catch (Exception) { return []; }
    }

    private static bool IsReparsePoint(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception) { return true; }
    }

    private static long SafeFileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception) { return 0; }
    }

    private static DateTimeOffset? SafeLastWrite(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch (Exception) { return null; }
    }

    private static string Bound(string value) => value.Trim()[..Math.Min(value.Trim().Length, 256)];

    private static string? BoundNullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : Bound(value);
}

public static class InoTecInvestigationPrivacy
{
    public static string HashIdentifier(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant()))).ToLowerInvariant();

    public static string HashOrUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown" : HashIdentifier(value);
}

public static class PortableExecutableExportReader
{
    private const int MaximumExportNames = 512;
    private const int MaximumExportNameLength = 256;
    private const long MaximumPortableExecutableBytes = 128 * 1024 * 1024;

    public static IReadOnlyList<string> ReadExportNames(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaximumPortableExecutableBytes) return [];
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            var directory = reader.PEHeaders.PEHeader?.ExportTableDirectory;
            if (directory is null || directory.Value.RelativeVirtualAddress == 0 || directory.Value.Size < 40) return [];
            var exportOffset = RvaToOffset(reader.PEHeaders, directory.Value.RelativeVirtualAddress);
            if (exportOffset < 0) return [];
            var header = Read(stream, exportOffset, 40);
            var numberOfNames = (int)Math.Min(
                BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(24, 4)),
                (uint)MaximumExportNames);
            var namesRva = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(32, 4));
            var namesOffset = RvaToOffset(reader.PEHeaders, checked((int)namesRva));
            if (namesOffset < 0) return [];

            var names = new List<string>();
            for (var index = 0; index < numberOfNames; index++)
            {
                var pointer = Read(stream, namesOffset + index * 4, 4);
                var nameRva = BinaryPrimitives.ReadUInt32LittleEndian(pointer);
                var nameOffset = RvaToOffset(reader.PEHeaders, checked((int)nameRva));
                var name = nameOffset < 0 ? null : ReadNullTerminatedAscii(stream, nameOffset);
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
            return names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static int RvaToOffset(PEHeaders headers, int rva)
    {
        foreach (var section in headers.SectionHeaders)
        {
            var size = Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + size)
            {
                return checked(rva - section.VirtualAddress + section.PointerToRawData);
            }
        }
        return -1;
    }

    private static byte[] Read(Stream stream, long offset, int count)
    {
        var buffer = new byte[count];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static string? ReadNullTerminatedAscii(Stream stream, long offset)
    {
        stream.Position = offset;
        var bytes = new List<byte>();
        for (var index = 0; index < MaximumExportNameLength; index++)
        {
            var value = stream.ReadByte();
            if (value <= 0) break;
            if (value is < 32 or > 126) return null;
            bytes.Add((byte)value);
        }
        return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
    }
}
