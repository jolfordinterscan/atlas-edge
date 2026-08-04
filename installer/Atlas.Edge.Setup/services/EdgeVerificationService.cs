using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;

namespace Atlas.Edge.Setup.Services;

public sealed class EdgeVerificationService
{
    private const string ServiceName = "Atlas Edge Runtime";

    private const string EnrollmentRegistryPath =
        @"SOFTWARE\InterScan\Atlas Edge\Enrollment";

    private static readonly string CredentialPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "InterScan",
        "Atlas Edge",
        "identity",
        "credentials.protected.bin");

    public async Task<EdgeVerificationResult> VerifyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var serviceRunning = IsServiceRunning();
            var credentialsExist = HasProtectedCredentials();
            var enrollmentTokenCleared = !HasEnrollmentToken();

            if (serviceRunning &&
                credentialsExist &&
                enrollmentTokenCleared)
            {
                return new EdgeVerificationResult(
                    ServiceRunning: true,
                    CredentialsCreated: true,
                    EnrollmentTokenCleared: true,
                    CredentialPath,
                    Error: null);
            }

            await Task.Delay(
                TimeSpan.FromSeconds(2),
                cancellationToken);
        }

        return new EdgeVerificationResult(
            ServiceRunning: IsServiceRunning(),
            CredentialsCreated: HasProtectedCredentials(),
            EnrollmentTokenCleared: !HasEnrollmentToken(),
            CredentialPath,
            Error: "Atlas Edge did not finish local enrollment verification before the timeout.");
    }

    private static bool IsServiceRunning()
    {
        try
        {
            using var service = new ServiceController(ServiceName);
            service.Refresh();

            return service.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool HasProtectedCredentials()
    {
        var file = new FileInfo(CredentialPath);

        return file.Exists && file.Length > 0;
    }

    private static bool HasEnrollmentToken()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            EnrollmentRegistryPath);

        return key?.GetValue("EnrollmentCode") is string token &&
               !string.IsNullOrWhiteSpace(token);
    }
}

public sealed record EdgeVerificationResult(
    bool ServiceRunning,
    bool CredentialsCreated,
    bool EnrollmentTokenCleared,
    string CredentialPath,
    string? Error)
{
    public bool Succeeded =>
        ServiceRunning &&
        CredentialsCreated &&
        EnrollmentTokenCleared;
}