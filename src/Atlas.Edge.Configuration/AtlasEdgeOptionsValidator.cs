using Microsoft.Extensions.Options;
using Atlas.Edge.Core;

namespace Atlas.Edge.Configuration;

public sealed class AtlasEdgeOptionsValidator : IValidateOptions<AtlasEdgeOptions>
{
    private static readonly string[] EvidenceProviders =
    [
        "Wia",
        "Twain",
        "Isis",
        "WindowsPnp",
        "WindowsDriver",
        "WindowsService",
        "WindowsEventLog",
        "LocalLog",
        "Registry",
        "Network",
        "Mock"
    ];

    public ValidateOptionsResult Validate(string? name, AtlasEdgeOptions options)
    {
        var errors = new List<string>();
        var isHttpTransport = string.Equals(
            options.TransportMode,
            AtlasEdgeOptions.TransportModeHttp,
            StringComparison.OrdinalIgnoreCase);
        var allowInsecureHttp = EndpointSecurityPolicy.IsDevelopmentOverrideEnabled(
            options.EnvironmentName,
            options.AllowInsecureHttpForDevelopment);

        if (!isHttpTransport && string.IsNullOrWhiteSpace(options.AgentId))
        {
            errors.Add("AgentId is required for null transport fallback.");
        }

        if (!isHttpTransport && string.IsNullOrWhiteSpace(options.WorkstationId))
        {
            errors.Add("WorkstationId is required for null transport fallback.");
        }

        if (!isHttpTransport && string.IsNullOrWhiteSpace(options.TenantBinding))
        {
            errors.Add("TenantBinding is required for null transport fallback.");
        }

        if (!isHttpTransport)
        {
            if (string.IsNullOrWhiteSpace(options.IngestionUrl))
            {
                errors.Add("IngestionUrl is required for null transport fallback.");
            }
            else if (!Uri.TryCreate(options.IngestionUrl, UriKind.Absolute, out var ingestionUri))
            {
                errors.Add("IngestionUrl must be an absolute URI.");
            }
            else if (!new EndpointSecurityPolicy(allowInsecureHttp).IsAllowed(ingestionUri))
            {
                errors.Add("IngestionUrl must use HTTPS unless the Development HTTP override is enabled.");
            }
        }

        if (options.HeartbeatIntervalSeconds <= 0)
        {
            errors.Add("HeartbeatIntervalSeconds must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.EnrollmentUrl))
        {
            errors.Add("EnrollmentUrl is required.");
        }
        else if (!Uri.TryCreate(options.EnrollmentUrl, UriKind.Absolute, out var enrollmentUri))
        {
            errors.Add("EnrollmentUrl must be an absolute URI.");
        }
        else if (!new EndpointSecurityPolicy(allowInsecureHttp).IsAllowed(enrollmentUri))
        {
            errors.Add("EnrollmentUrl must use HTTPS unless the Development HTTP override is enabled.");
        }

        if (options.AllowInsecureHttpForDevelopment && !allowInsecureHttp)
        {
            errors.Add("AllowInsecureHttpForDevelopment can only be enabled when EnvironmentName is Development.");
        }

        if (options.HttpTimeoutSeconds <= 0)
        {
            errors.Add("HttpTimeoutSeconds must be greater than zero.");
        }

        if (options.TokenRefreshLeadTimeSeconds < options.TokenClockSkewSeconds)
        {
            errors.Add("TokenRefreshLeadTimeSeconds must be greater than or equal to TokenClockSkewSeconds.");
        }

        if (options.TokenRefreshRetryMaxSeconds < options.TokenRefreshRetryBaseSeconds)
        {
            errors.Add("TokenRefreshRetryMaxSeconds must be greater than or equal to TokenRefreshRetryBaseSeconds.");
        }

        if (!string.Equals(options.TransportMode, AtlasEdgeOptions.TransportModeNull, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.TransportMode, AtlasEdgeOptions.TransportModeHttp, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("TransportMode must be either Null or Http.");
        }

        if (string.IsNullOrWhiteSpace(options.SiteTimezone))
        {
            errors.Add("SiteTimezone is required.");
        }

        if (options.QueueBatchSize <= 0)
        {
            errors.Add("QueueBatchSize must be greater than zero.");
        }

        if (!string.IsNullOrWhiteSpace(options.EventQueueStorePath) &&
            !Path.IsPathFullyQualified(options.EventQueueStorePath))
        {
            errors.Add("EventQueueStorePath must be an absolute path when configured.");
        }

        if (options.QueueRetryMaximumSeconds < options.QueueRetryBaseSeconds)
        {
            errors.Add("QueueRetryMaximumSeconds must be greater than or equal to QueueRetryBaseSeconds.");
        }

        if (!string.Equals(
                options.ScannerDiscoveryProvider,
                AtlasEdgeOptions.ScannerDiscoveryProviderPlatform,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                options.ScannerDiscoveryProvider,
                AtlasEdgeOptions.ScannerDiscoveryProviderMock,
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ScannerDiscoveryProvider must be either Platform or Mock.");
        }

        if (string.Equals(
                options.ScannerDiscoveryProvider,
                AtlasEdgeOptions.ScannerDiscoveryProviderMock,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ScannerDiscoveryProvider Mock can only be used in the Development environment.");
        }

        var allowedDiscoveryProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Wia", "Twain", "Isis"
        };
        if (options.ScannerDiscoveryProviders.Any(provider => !allowedDiscoveryProviders.Contains(provider)))
        {
            errors.Add("ScannerDiscoveryProviders may contain only Wia, Twain, or Isis.");
        }

        if (!string.Equals(
                options.ScannerInventoryPublishMode,
                AtlasEdgeOptions.ScannerInventoryPublishModeDisabled,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                options.ScannerInventoryPublishMode,
                AtlasEdgeOptions.ScannerInventoryPublishModeQueueOnly,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                options.ScannerInventoryPublishMode,
                AtlasEdgeOptions.ScannerInventoryPublishModeTransport,
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ScannerInventoryPublishMode must be Disabled, QueueOnly, or Transport.");
        }


        if (!string.Equals(
                options.ScannerHealthProvider,
                AtlasEdgeOptions.ScannerHealthProviderPlatform,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                options.ScannerHealthProvider,
                AtlasEdgeOptions.ScannerHealthProviderMock,
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ScannerHealthProvider must be either Platform or Mock.");
        }

        if (string.Equals(
                options.ScannerHealthProvider,
                AtlasEdgeOptions.ScannerHealthProviderMock,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ScannerHealthProvider Mock can only be used in the Development environment.");
        }

        if (!string.Equals(
                options.ScannerConnectorProvider,
                AtlasEdgeOptions.ScannerConnectorProviderPlatform,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                options.ScannerConnectorProvider,
                AtlasEdgeOptions.ScannerConnectorProviderMock,
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ScannerConnectorProvider must be either Platform or Mock.");
        }

        if (string.Equals(
                options.ScannerConnectorProvider,
                AtlasEdgeOptions.ScannerConnectorProviderMock,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ScannerConnectorProvider Mock can only be used in the Development environment.");
        }

        ValidateScannerEvidence(options, errors);

        if (string.IsNullOrWhiteSpace(options.EnvironmentName))
        {
            errors.Add("EnvironmentName is required.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateScannerEvidence(AtlasEdgeOptions options, List<string> errors)
    {
        var mockMode = string.Equals(
            options.ScannerEvidenceMode,
            AtlasEdgeOptions.ScannerEvidenceModeMock,
            StringComparison.OrdinalIgnoreCase);
        var platformMode = string.Equals(
            options.ScannerEvidenceMode,
            AtlasEdgeOptions.ScannerEvidenceModePlatform,
            StringComparison.OrdinalIgnoreCase);
        var development = string.Equals(options.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase);

        if (!mockMode && !platformMode)
        {
            errors.Add("ScannerEvidenceMode must be either Platform or Mock.");
        }

        if (mockMode && !development)
        {
            errors.Add("ScannerEvidenceMode Mock can only be used in the Development environment.");
        }

        foreach (var provider in options.ScannerEvidenceProviders ?? [])
        {
            if (!EvidenceProviders.Contains(provider, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"ScannerEvidenceProviders contains unknown provider '{provider}'.");
            }
        }

        if ((options.ScannerEvidenceProviders ?? []).Contains("Mock", StringComparer.OrdinalIgnoreCase) &&
            (!mockMode || !development))
        {
            errors.Add("Scanner evidence provider Mock requires Development mock mode.");
        }

        foreach (var path in options.ScannerEvidenceLogDirectories ?? [])
        {
            if (!IsSafeEvidenceDirectory(path))
            {
                errors.Add("ScannerEvidenceLogDirectories must contain explicit non-root absolute paths without wildcards or traversal.");
            }
        }

        foreach (var path in options.ScannerEvidenceLogFiles ?? [])
        {
            if (!IsSafeEvidenceFile(path, options.ScannerEvidenceLogDirectories ?? []))
            {
                errors.Add("ScannerEvidenceLogFiles must be absolute files inside an allowlisted log directory.");
            }
        }

        foreach (var path in options.ScannerEvidenceRegistryPaths ?? [])
        {
            if (!IsSafeRegistryPath(path))
            {
                errors.Add("ScannerEvidenceRegistryPaths must contain explicit HKLM subkeys without wildcards or traversal.");
            }
        }

        if ((options.ScannerEvidenceServiceNames ?? []).Any(value => !IsSafeAllowlistName(value)))
        {
            errors.Add("ScannerEvidenceServiceNames must contain explicit service names.");
        }

        if ((options.ScannerEvidenceEventLogChannels ?? []).Any(value => !IsSafeAllowlistName(value)) ||
            (options.ScannerEvidenceEventLogProviders ?? []).Any(value => !IsSafeAllowlistName(value)))
        {
            errors.Add("Scanner evidence Event Log channels and providers must be explicitly allowlisted.");
        }

        foreach (var target in options.ScannerEvidenceNetworkTargets ?? [])
        {
            if (!IsSafeNetworkTarget(target, development && mockMode, options.ScannerEvidenceSnmpEnabled))
            {
                errors.Add("ScannerEvidenceNetworkTargets must use HTTPS, configured SNMP, or loopback HTTP in Development mock mode.");
            }
        }

        if (options.ScannerEvidenceAllowTlsBypass)
        {
            errors.Add("ScannerEvidenceAllowTlsBypass is prohibited.");
        }

        if (options.ScannerEvidenceMaximumReadBytes > options.ScannerEvidenceMaximumFileSizeBytes)
        {
            errors.Add("ScannerEvidenceMaximumReadBytes must not exceed ScannerEvidenceMaximumFileSizeBytes.");
        }
    }

    private static bool IsSafeEvidenceDirectory(string? path)
    {
        if (!IsSafeAbsolutePath(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path!);
        return !string.Equals(fullPath, Path.GetPathRoot(fullPath), PathComparison);
    }

    private static bool IsSafeEvidenceFile(string? path, IEnumerable<string> directories)
    {
        if (!IsSafeAbsolutePath(path))
        {
            return false;
        }

        var file = Path.GetFullPath(path!);
        return directories.Where(IsSafeEvidenceDirectory).Any(directory =>
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            return file.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);
        });
    }

    private static bool IsSafeRegistryPath(string? path)
    {
        if (!IsSafeAllowlistName(path))
        {
            return false;
        }

        var normalized = path!.Trim().TrimEnd('\\');
        var unrestricted = new[]
        {
            "HKLM",
            "HKLM\\SOFTWARE",
            "HKLM\\SYSTEM",
            "HKEY_LOCAL_MACHINE",
            "HKEY_LOCAL_MACHINE\\SOFTWARE",
            "HKEY_LOCAL_MACHINE\\SYSTEM"
        };
        return !unrestricted.Contains(normalized, StringComparer.OrdinalIgnoreCase) &&
            (normalized.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSafeNetworkTarget(string? value, bool allowLocalHttp, bool snmpEnabled)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            (allowLocalHttp && uri.IsLoopback &&
                string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) ||
            (snmpEnabled && string.Equals(uri.Scheme, "snmp", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSafeAbsolutePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.IsPathFullyQualified(path) &&
        IsSafeAllowlistName(path);

    private static bool IsSafeAllowlistName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Contains('*') &&
        !value.Contains('?') &&
        !value.Replace('\\', '/').Split('/').Any(segment => segment == "..");

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
