namespace Atlas.Edge.ScannerEvidence;

public static class EvidenceSafetyPolicy
{
    private static readonly string[] UnrestrictedRegistryPaths =
    [
        "HKEY_LOCAL_MACHINE",
        "HKEY_LOCAL_MACHINE\\SOFTWARE",
        "HKEY_LOCAL_MACHINE\\SYSTEM",
        "HKLM",
        "HKLM\\SOFTWARE",
        "HKLM\\SYSTEM"
    ];

    public static bool IsSafeAllowlistedDirectory(string? path)
    {
        if (!IsSafeAbsolutePath(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path!);
        return !string.Equals(fullPath, Path.GetPathRoot(fullPath), PathComparison);
    }

    public static bool IsSafeAllowlistedFile(string? path, IEnumerable<string> allowedDirectories)
    {
        if (!IsSafeAbsolutePath(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path!);
        return allowedDirectories.Any(directory => IsWithin(fullPath, directory));
    }

    public static bool IsSafeRegistryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || ContainsWildcardOrTraversal(path))
        {
            return false;
        }

        var normalized = path.Trim().TrimEnd('\\');
        if (UnrestrictedRegistryPaths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return normalized.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSafeAllowlistName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !ContainsWildcardOrTraversal(value);

    public static bool IsSafeNetworkTarget(
        string? value,
        bool allowLocalDevelopmentHttp,
        bool snmpEnabled)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return allowLocalDevelopmentHttp && uri.IsLoopback;
        }

        return snmpEnabled && string.Equals(uri.Scheme, "snmp", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsWithin(string filePath, string allowedDirectory)
    {
        if (!IsSafeAllowlistedDirectory(allowedDirectory))
        {
            return false;
        }

        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedDirectory));
        var file = Path.GetFullPath(filePath);
        return file.StartsWith(directory + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool IsSafeAbsolutePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.IsPathFullyQualified(path) &&
        !ContainsWildcardOrTraversal(path);

    private static bool ContainsWildcardOrTraversal(string value) =>
        value.Contains('*') ||
        value.Contains('?') ||
        value.Replace('\\', '/').Split('/')
            .Any(segment => segment == "..");

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
