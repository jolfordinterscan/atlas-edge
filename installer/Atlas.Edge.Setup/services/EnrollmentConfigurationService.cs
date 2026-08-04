using Microsoft.Win32;

namespace Atlas.Edge.Setup.Services;

public sealed class EnrollmentConfigurationService
{
    private const string RegistryPath =
        @"SOFTWARE\InterScan\Atlas Edge\Enrollment";

    public void Save(
        string atlasServer,
        string enrollmentToken)
    {
        if (!Uri.TryCreate(
                atlasServer.Trim(),
                UriKind.Absolute,
                out var serverUri) ||
            serverUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Enter a valid HTTPS Atlas server URL.");
        }

        if (string.IsNullOrWhiteSpace(enrollmentToken))
        {
            throw new InvalidOperationException(
                "Enter the one-time Atlas enrollment token.");
        }

        var baseUrl =
            serverUri.GetLeftPart(UriPartial.Authority) + "/";

        using var key = Registry.LocalMachine.CreateSubKey(
            RegistryPath,
            writable: true);

        if (key is null)
        {
            throw new UnauthorizedAccessException(
                "Atlas Edge enrollment configuration could not be created.");
        }

        key.SetValue(
            "EnrollmentCode",
            enrollmentToken.Trim(),
            RegistryValueKind.String);

        key.SetValue(
            "EnrollmentUrl",
            baseUrl,
            RegistryValueKind.String);

        key.SetValue(
            "IngestionUrl",
            baseUrl,
            RegistryValueKind.String);

        key.SetValue(
            "TransportMode",
            "Http",
            RegistryValueKind.String);
    }

    public bool HasEnrollmentToken()
    {
        using var key =
            Registry.LocalMachine.OpenSubKey(RegistryPath);

        return key?.GetValue("EnrollmentCode") is string token &&
               !string.IsNullOrWhiteSpace(token);
    }

    public void ClearEnrollmentToken()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            RegistryPath,
            writable: true);

        key?.DeleteValue(
            "EnrollmentCode",
            throwOnMissingValue: false);
    }
}