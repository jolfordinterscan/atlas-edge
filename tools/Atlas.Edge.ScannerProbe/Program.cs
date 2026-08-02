using Atlas.Edge.ScannerDiscovery;
using Microsoft.Extensions.Logging.Abstractions;

Console.WriteLine("Atlas Edge Scanner Probe");
Console.WriteLine("Read-only enumeration; image acquisition and scanner commands are not available.");
var metadataDiagnostics = args.Contains("--metadata-diagnostics", StringComparer.OrdinalIgnoreCase);

var timeout = TimeSpan.FromSeconds(15);
var metadataEnricher = new ScannerMetadataEnricher(
    [
        new WindowsPnpScannerMetadataProvider(new WindowsPnpScannerMetadataCatalog()),
        new WindowsRegistryScannerMetadataProvider(new WindowsScannerRegistryMetadataCatalog())
    ],
    TimeProvider.System,
    timeout);
var service = new ScannerDiscoveryService(
    [new WiaScannerDiscoveryAdapter(new WiaScannerSourceCatalog())],
    TimeProvider.System,
    NullLogger<ScannerDiscoveryService>.Instance,
    new ScannerIdentityFactory(),
    timeout,
    metadataEnricher);

VendorInstallationSnapshot vendorInstallations;
using (var vendorCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
{
    try
    {
        vendorInstallations = await VendorInstallationCatalog.CreateWindowsDefault()
            .DiscoverAsync(vendorCancellation.Token);
    }
    catch (OperationCanceledException)
    {
        vendorInstallations = new VendorInstallationSnapshot(
            false,
            [],
            [new VendorInstallationDiagnostic("vendor_probe_timeout", "WindowsVendorInstallationSource")]);
    }
}

Console.WriteLine();
Console.WriteLine($"Vendor installation catalog available: {vendorInstallations.IsAvailable}");
Console.WriteLine($"Installed vendor components: {vendorInstallations.Installations.Count}");
foreach (var installation in vendorInstallations.Installations)
{
    Console.WriteLine();
    Console.WriteLine($"Vendor component: {installation.Vendor}");
    Console.WriteLine($"Product: {installation.ProductName}");
    Console.WriteLine($"Version: {Value(installation.Version)}");
    Console.WriteLine($"Install path: {installation.InstallPath}");
    Console.WriteLine($"Architecture: {installation.Architecture}");
    Console.WriteLine($"Discovery source: {installation.Source}");
    Console.WriteLine($"SDK candidates: {installation.SdkCandidates.Count}");
    foreach (var candidate in installation.SdkCandidates)
    {
        Console.WriteLine($"  {candidate.Name} ({candidate.InterfaceKind}, {candidate.Architecture}, {Value(candidate.Version)})");
    }
}

foreach (var provider in VendorMetadataProviderFactory.CreateDetectionProviders())
{
    var status = provider.Detect(vendorInstallations);
    Console.WriteLine();
    Console.WriteLine($"Vendor Provider: {status.ProviderName}");
    Console.WriteLine($"Installed: {(status.IsInstalled ? "Yes" : "No")}");
    Console.WriteLine($"Provider availability: {status.Availability}");
    Console.WriteLine($"Metadata Available: {FormatFields(status, VendorMetadataAvailability.Available)}");
    Console.WriteLine($"Unavailable: {FormatFields(status, VendorMetadataAvailability.Unavailable)}");
    Console.WriteLine($"Unsupported: {FormatFields(status, VendorMetadataAvailability.Unsupported)}");
    Console.WriteLine($"Unknown: {FormatFields(status, VendorMetadataAvailability.Unknown)}");
}

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
try
{
    var snapshot = await service.DiscoverAsync(cancellation.Token);
    var diagnostic = snapshot.Diagnostics.Single();
    Console.WriteLine($"Provider: {diagnostic.Protocol}");
    Console.WriteLine($"Provider available: {diagnostic.IsAvailable}");
    Console.WriteLine($"Scanners discovered: {snapshot.Scanners.Count}");

    foreach (var scanner in snapshot.Scanners)
    {
        Console.WriteLine();
        Console.WriteLine($"Manufacturer: {scanner.Manufacturer}");
        Console.WriteLine($"Model: {scanner.Model}");
        Console.WriteLine($"Serial: {ScannerMetadataPrivacy.MaskSerial(scanner.SerialNumber)}");
        Console.WriteLine($"Serial source: {Value(scanner.SerialSource)}");
        Console.WriteLine($"Friendly name: {Value(scanner.FriendlyName)}");
        Console.WriteLine($"Driver: {Value(scanner.Drivers.FirstOrDefault()?.Name)}");
        Console.WriteLine($"Driver provider: {Value(scanner.DriverProvider)}");
        Console.WriteLine($"Driver version: {Value(scanner.Drivers.FirstOrDefault()?.Version)}");
        Console.WriteLine($"USB VID: {Value(scanner.UsbVendorId)}");
        Console.WriteLine($"USB PID: {Value(scanner.UsbProductId)}");
        Console.WriteLine($"Location hash: {Value(scanner.LocationPathHash)}");
        Console.WriteLine($"Container ID hash: {Value(scanner.ContainerId)}");
        Console.WriteLine($"Device instance ID hash: {Value(scanner.DeviceInstanceIdHash)}");
        Console.WriteLine($"Connection: {scanner.ConnectionType}");
        Console.WriteLine($"Status: {scanner.Status}");
        Console.WriteLine($"Capabilities: {FormatCapabilities(scanner.NormalizedCapabilities)}");
        Console.WriteLine($"Stable Scanner ID: {scanner.DiscoveryId}");
        if (metadataDiagnostics)
        {
            var matches = scanner.MetadataDiagnostics
                .Where(value => value.MatchStrategy != "Unavailable")
                .ToArray();
            if (matches.Length == 0)
            {
                Console.WriteLine("Metadata match: None");
            }
            foreach (var match in matches)
            {
                Console.WriteLine($"Metadata match: {match.ProviderName}");
                Console.WriteLine($"Match strategy: {match.MatchStrategy}");
                Console.WriteLine($"Match score: {match.MatchScore}");
                Console.WriteLine($"Candidates evaluated: {match.CandidatesEvaluated}");
                Console.WriteLine($"Ambiguous: {match.IsAmbiguous}");
                Console.WriteLine($"Populated fields: {FormatValues(match.PopulatedFields)}");
            }
        }
    }

    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Scanner discovery timed out or was canceled.");
    return 2;
}

static string FormatCapabilities(IReadOnlyList<ScannerCapability> capabilities) =>
    capabilities.Count == 0 ? "Unknown" : string.Join(", ", capabilities);

static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;

static string FormatValues(IReadOnlyList<string> values) =>
    values.Count == 0 ? "None" : string.Join(", ", values);

static string FormatFields(VendorMetadataProviderStatus status, VendorMetadataAvailability availability)
{
    var fields = status.Capabilities
        .Where(value => value.Availability == availability)
        .Select(value => value.Field)
        .ToArray();
    return fields.Length == 0 ? "None" : string.Join(", ", fields);
}
