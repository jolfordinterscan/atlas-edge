using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Atlas.Edge.ScannerDiscovery;

public sealed class WiaScannerSourceCatalog : IWiaScannerSourceCatalog
{
    public Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new ScannerSourceCatalogResult(false, Array.Empty<ScannerSourceMetadata>()));
        }

        return EnumerateOnStaThreadAsync(cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static async Task<ScannerSourceCatalogResult> EnumerateOnStaThreadAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ScannerSourceCatalogResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(EnumerateWindowsDevices());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Atlas Edge WIA discovery"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        using var registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        return await completion.Task.ConfigureAwait(false);
    }

    [SupportedOSPlatform("windows")]
    private static ScannerSourceCatalogResult EnumerateWindowsDevices()
    {
        var managerType = Type.GetTypeFromProgID("WIA.DeviceManager");
        if (managerType is null)
        {
            return new ScannerSourceCatalogResult(false, Array.Empty<ScannerSourceMetadata>());
        }

        object? manager = null;
        object? deviceInfos = null;
        var sources = new List<ScannerSourceMetadata>();
        try
        {
            manager = Activator.CreateInstance(managerType);
            if (manager is null)
            {
                return new ScannerSourceCatalogResult(false, Array.Empty<ScannerSourceMetadata>());
            }

            dynamic dynamicManager = manager;
            deviceInfos = dynamicManager.DeviceInfos;
            foreach (var deviceInfoObject in (dynamic)deviceInfos)
            {
                object? deviceInfo = deviceInfoObject;
                try
                {
                    dynamic device = deviceInfoObject;
                    if (Convert.ToInt32(device.Type) != 1)
                    {
                        continue;
                    }

                    var properties = ReadProperties(device);
                    sources.Add(CreateSource(properties));
                }
                catch (Exception ex) when (ex is COMException or FormatException or InvalidCastException)
                {
                    // A malformed or disconnected WIA record is skipped without exposing platform details.
                }
                finally
                {
                    ReleaseComObject(deviceInfo);
                }
            }
        }
        finally
        {
            ReleaseComObject(deviceInfos);
            ReleaseComObject(manager);
        }

        return new ScannerSourceCatalogResult(true, sources.AsReadOnly());
    }

    [SupportedOSPlatform("windows")]
    private static Dictionary<string, string> ReadProperties(dynamic device)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        object? propertyCollection = null;
        try
        {
            propertyCollection = device.Properties;
            foreach (var propertyObject in (dynamic)propertyCollection)
            {
                object? comProperty = propertyObject;
                try
                {
                    dynamic property = propertyObject;
                    var name = Convert.ToString(property.Name);
                    var value = Convert.ToString(property.Value);
                    if (!string.IsNullOrWhiteSpace(name) && value is not null)
                    {
                        properties[name] = value;
                    }
                }
                catch (COMException)
                {
                    // Some optional WIA properties throw when read. Their absence is represented as unknown.
                }
                finally
                {
                    ReleaseComObject(comProperty);
                }
            }
        }
        finally
        {
            ReleaseComObject(propertyCollection);
        }

        return properties;
    }

    private static ScannerSourceMetadata CreateSource(IReadOnlyDictionary<string, string> properties)
    {
        var manufacturer = Find(properties, "Manufacturer", "Vendor Description") ?? "Unknown";
        var model = Find(properties, "Description", "Name", "Device Description") ?? "Unknown";
        var serialNumber = Find(properties, "Serial Number", "SerialNumber");
        var firmware = Find(properties, "Firmware Version", "Firmware");
        var port = Find(properties, "Port Name", "Port", "PnP ID");
        var stableSourceId = Find(properties, "Device ID", "Unique Device ID", "PnP ID");
        var sourceId = stableSourceId ?? CreatePropertyFingerprint(properties);
        var handling = Find(properties, "Document Handling Capabilities");
        var handlingFlags = int.TryParse(handling, out var parsedHandling) ? parsedHandling : (int?)null;
        var capabilities = new List<string>();

        if (handlingFlags.HasValue)
        {
            if ((handlingFlags.Value & 1) != 0)
            {
                capabilities.Add("automatic-document-feeder");
            }

            if ((handlingFlags.Value & 2) != 0)
            {
                capabilities.Add("flatbed");
            }

            if ((handlingFlags.Value & 4) != 0)
            {
                capabilities.Add("duplex");
            }
        }

        return new ScannerSourceMetadata(
            sourceId,
            manufacturer,
            model,
            serialNumber,
            firmware,
            InferInterface(port),
            handlingFlags.HasValue ? (handlingFlags.Value & 4) != 0 : null,
            null,
            handlingFlags.HasValue ? (handlingFlags.Value & 1) != 0 : null,
            capabilities,
            new ScannerDriver(
                Find(properties, "Driver Name", "Name") ?? model,
                Find(properties, "Driver Version", "STI Driver Version"),
                manufacturer),
            ScannerOnlineStatus.Unknown)
        {
            DevicePath = port,
            HasProviderStableIdentity = stableSourceId is not null
        };
    }

    private static string? Find(IReadOnlyDictionary<string, string> properties, params string[] names)
    {
        foreach (var name in names)
        {
            var match = properties.FirstOrDefault(property =>
                property.Key.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                return match.Value.Trim();
            }
        }

        return null;
    }

    private static string InferInterface(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        if (value.Contains("USB", StringComparison.OrdinalIgnoreCase))
        {
            return "USB";
        }

        if (value.Contains("SCSI", StringComparison.OrdinalIgnoreCase))
        {
            return "SCSI";
        }

        if (value.Contains("TCP", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("NETWORK", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("IP_", StringComparison.OrdinalIgnoreCase))
        {
            return "Network";
        }

        return value.Trim();
    }

    private static string CreatePropertyFingerprint(IReadOnlyDictionary<string, string> properties)
    {
        var normalized = string.Join('|', properties
            .OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase)
            .Select(property => $"{property.Key.Trim().ToUpperInvariant()}={property.Value.Trim().ToUpperInvariant()}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"wia-metadata-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    [SupportedOSPlatform("windows")]
    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
