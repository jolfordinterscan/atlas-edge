using System.Text.Json;
using System.Text.Json.Serialization;
using Atlas.Edge.ScannerDiscovery;

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() }
};

using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
try
{
    var snapshot = await InoTecInvestigator.CreateWindowsDefault().InspectAsync(timeout.Token);
    Console.WriteLine(JsonSerializer.Serialize(snapshot, options));
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("InoTec investigation did not finish within the bounded probe timeout.");
    return 2;
}
catch (Exception)
{
    Console.Error.WriteLine("InoTec investigation failed with sanitized error code inotec_probe_failure.");
    return 1;
}
