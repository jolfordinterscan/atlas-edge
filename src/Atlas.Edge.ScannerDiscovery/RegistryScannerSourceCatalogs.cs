using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Atlas.Edge.ScannerDiscovery;

public sealed class TwainScannerSourceCatalog : ITwainScannerSourceCatalog
{
    private static readonly string[] RegistryPaths =
    [
        @"SOFTWARE\TWAIN\Sources",
        @"SOFTWARE\TWAIN\DSM\Sources"
    ];

    public Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new ScannerSourceCatalogResult(false, Array.Empty<ScannerSourceMetadata>()));
        }

        var registryResult = RegistryScannerSourceReader.Read(RegistryPaths, cancellationToken);
        var fileResult = ReadTwainSourceFiles(cancellationToken);
        var result = new ScannerSourceCatalogResult(
            registryResult.IsAvailable || fileResult.IsAvailable,
            registryResult.Sources
                .Concat(fileResult.Sources)
                .GroupBy(source => source.SourceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray());
        return Task.FromResult(result);
    }

    [SupportedOSPlatform("windows")]
    private static ScannerSourceCatalogResult ReadTwainSourceFiles(CancellationToken cancellationToken)
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory))
        {
            return new ScannerSourceCatalogResult(false, Array.Empty<ScannerSourceMetadata>());
        }

        var sourceDirectories = new[]
        {
            Path.Combine(windowsDirectory, "twain_32"),
            Path.Combine(windowsDirectory, "twain_64")
        };
        var dsmPaths = new[]
        {
            Path.Combine(windowsDirectory, "twain_32.dll"),
            Path.Combine(windowsDirectory, "System32", "twaindsm.dll"),
            Path.Combine(windowsDirectory, "SysWOW64", "twaindsm.dll")
        };
        var isAvailable = dsmPaths.Any(File.Exists) || sourceDirectories.Any(Directory.Exists);
        var sources = new List<ScannerSourceMetadata>();

        foreach (var directory in sourceDirectories.Where(Directory.Exists))
        {
            IEnumerable<string> sourceFiles;
            try
            {
                sourceFiles = Directory.EnumerateFiles(directory, "*.ds", SearchOption.AllDirectories).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var sourceFile in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileVersionInfo versionInfo;
                try
                {
                    versionInfo = FileVersionInfo.GetVersionInfo(sourceFile);
                }
                catch (FileNotFoundException)
                {
                    continue;
                }
                catch (SystemException)
                {
                    continue;
                }

                var sourceName = Path.GetFileNameWithoutExtension(sourceFile);
                var manufacturer = FirstValue(versionInfo.CompanyName, new DirectoryInfo(Path.GetDirectoryName(sourceFile)!).Name) ??
                    "Unknown";
                var model = FirstValue(versionInfo.ProductName, versionInfo.FileDescription, sourceName) ?? "Unknown";
                sources.Add(new ScannerSourceMetadata(
                    sourceFile,
                    manufacturer,
                    model,
                    null,
                    null,
                    "Unknown",
                    null,
                    null,
                    null,
                    Array.Empty<string>(),
                    new ScannerDriver(
                        FirstValue(versionInfo.FileDescription, sourceName) ?? sourceName,
                        FirstValue(versionInfo.FileVersion, versionInfo.ProductVersion),
                        FirstValue(versionInfo.CompanyName, manufacturer)),
                    ScannerOnlineStatus.Unknown));
            }
        }

        return new ScannerSourceCatalogResult(isAvailable, sources);
    }

    private static string? FirstValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

public sealed class IsisScannerSourceCatalog : IIsisScannerSourceCatalog
{
    private static readonly string[] RegistryPaths =
    [
        @"SOFTWARE\Pixel Translations\ISIS\Scanners",
        @"SOFTWARE\EMC\Captiva\ISIS\Scanners",
        @"SOFTWARE\OpenText\Captiva\ISIS\Scanners"
    ];

    public Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken)
    {
        var result = OperatingSystem.IsWindows()
            ? RegistryScannerSourceReader.Read(RegistryPaths, cancellationToken)
            : new ScannerSourceCatalogResult(false, Array.Empty<ScannerSourceMetadata>());
        return Task.FromResult(result);
    }
}

[SupportedOSPlatform("windows")]
internal static class RegistryScannerSourceReader
{
    public static ScannerSourceCatalogResult Read(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ScannerSourceCatalogResult(false, Array.Empty<ScannerSourceMetadata>());
        }

        var isAvailable = false;
        var sources = new List<ScannerSourceMetadata>();

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var root = baseKey.OpenSubKey(path, writable: false);
                if (root is null)
                {
                    continue;
                }

                isAvailable = true;
                foreach (var sourceName in root.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var sourceKey = root.OpenSubKey(sourceName, writable: false);
                    if (sourceKey is not null)
                    {
                        sources.Add(CreateSource(sourceKey, $"{view}:{path}:{sourceName}", sourceName));
                    }
                }
            }
        }

        return new ScannerSourceCatalogResult(
            isAvailable,
            sources
                .GroupBy(source => source.SourceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray());
    }

    private static ScannerSourceMetadata CreateSource(
        RegistryKey key,
        string sourceId,
        string fallbackName)
    {
        var manufacturer = ReadString(key, "Manufacturer", "Vendor") ?? "Unknown";
        var model = ReadString(key, "Model", "ProductName", "Description") ?? fallbackName;
        var capabilities = ReadCapabilities(key);

        return new ScannerSourceMetadata(
            sourceId,
            manufacturer,
            model,
            ReadString(key, "SerialNumber", "Serial"),
            ReadString(key, "FirmwareVersion", "Firmware"),
            ReadString(key, "Interface", "Connection") ?? "Unknown",
            ReadBoolean(key, "Duplex", "SupportsDuplex"),
            ReadBoolean(key, "Color", "SupportsColor"),
            ReadBoolean(key, "Feeder", "HasFeeder"),
            capabilities,
            new ScannerDriver(
                ReadString(key, "DriverName", "Driver") ?? fallbackName,
                ReadString(key, "DriverVersion", "Version"),
                ReadString(key, "DriverProvider", "Provider") ?? manufacturer),
            ScannerOnlineStatus.Unknown,
            ReadMetadata(key));
    }

    private static string? ReadString(RegistryKey key, params string[] names)
    {
        foreach (var name in names)
        {
            var value = key.GetValue(name)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool? ReadBoolean(RegistryKey key, params string[] names)
    {
        var value = ReadString(key, names);
        if (value is null)
        {
            return null;
        }

        if (bool.TryParse(value, out var parsedBoolean))
        {
            return parsedBoolean;
        }

        return int.TryParse(value, out var parsedInteger) ? parsedInteger != 0 : null;
    }

    private static IReadOnlyList<string> ReadCapabilities(RegistryKey key)
    {
        var value = key.GetValue("Capabilities");
        return value switch
        {
            string[] values => values.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            string text => text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            _ => Array.Empty<string>()
        };
    }

    private static IReadOnlyDictionary<string, string> ReadMetadata(RegistryKey key)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var valueName in key.GetValueNames())
        {
            var value = key.GetValue(valueName)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                metadata[valueName] = value;
            }
        }

        return metadata;
    }
}
