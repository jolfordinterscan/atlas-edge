using Atlas.Edge.ScannerDiscovery;
using Microsoft.Extensions.Logging.Abstractions;

Console.WriteLine("Atlas Edge Scanner Probe");
Console.WriteLine("Read-only enumeration; image acquisition and scanner commands are not available.");

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
