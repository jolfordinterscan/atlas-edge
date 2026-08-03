using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Atlas.Edge.Runtime;

[SupportedOSPlatform("windows")]
internal static class WindowsMachineEnrollmentConfiguration
{
    private const string RegistryPath =
        @"SOFTWARE\InterScan\Atlas Edge\Enrollment";

    public static IReadOnlyDictionary<string, string?> Load()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RegistryPath);

        if (key is null)
        {
            return new Dictionary<string, string?>();
        }

        var configuration = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);

        AddIfPresent(
            configuration,
            "AtlasEdge:EnrollmentCode",
            key.GetValue("EnrollmentCode") as string);

        AddIfPresent(
            configuration,
            "AtlasEdge:EnrollmentUrl",
            key.GetValue("EnrollmentUrl") as string);

        AddIfPresent(
            configuration,
            "AtlasEdge:IngestionUrl",
            key.GetValue("IngestionUrl") as string);

        AddIfPresent(
            configuration,
            "AtlasEdge:TransportMode",
            key.GetValue("TransportMode") as string);

        return configuration;
    }

    public static void ClearEnrollmentCode()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            RegistryPath,
            writable: true);

        key?.DeleteValue(
            "EnrollmentCode",
            throwOnMissingValue: false);
    }

    private static void AddIfPresent(
        IDictionary<string, string?> configuration,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[key] = value;
        }
    }
}
