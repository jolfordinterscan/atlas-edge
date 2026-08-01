using Atlas.Edge.ScannerDiscovery;
using Microsoft.Extensions.Logging.Abstractions;

Console.WriteLine("Atlas Edge Scanner Probe");
Console.WriteLine("Read-only enumeration; image acquisition and scanner commands are not available.");

var service = new ScannerDiscoveryService(
    [new WiaScannerDiscoveryAdapter(new WiaScannerSourceCatalog())],
    TimeProvider.System,
    NullLogger<ScannerDiscoveryService>.Instance,
    new ScannerIdentityFactory(),
    TimeSpan.FromSeconds(15));

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
        Console.WriteLine($"Serial: {Mask(scanner.SerialNumber)}");
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

static string Mask(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "Unknown";
    }

    var suffixLength = Math.Min(4, value.Length);
    return $"****{value[^suffixLength..]}";
}

static string FormatCapabilities(IReadOnlyList<ScannerCapability> capabilities) =>
    capabilities.Count == 0 ? "Unknown" : string.Join(", ", capabilities);
