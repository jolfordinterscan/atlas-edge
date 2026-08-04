using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Atlas.Edge.Setup.Services;

public sealed class InstallerService
{
    private const int SuccessExitCode = 0;
    private const int SuccessRestartRequiredExitCode = 3010;

    public async Task<InstallerResult> InstallAsync(
        string msiPath,
        string logPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(msiPath))
        {
            return InstallerResult.Failure(
                "The Atlas Edge MSI path was not provided.");
        }

        var fullMsiPath = Path.GetFullPath(msiPath);

        if (!File.Exists(fullMsiPath))
        {
            return InstallerResult.Failure(
                $"Atlas Edge MSI was not found at: {fullMsiPath}");
        }

        var fullLogPath = Path.GetFullPath(logPath);
        var logDirectory = Path.GetDirectoryName(fullLogPath);

        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments =
                $"/i \"{fullMsiPath}\" " +
                "/qn /norestart " +
                $"/L*v \"{fullLogPath}\" " +
                "ATLAS_ACCEPT_EULA=1 " +
                "ATLAS_ACCEPT_TELEMETRY=1 " +
                "ATLAS_ADMIN_AUTHORIZED=1"
        };

        try
        {
            using var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            if (!process.Start())
            {
                return InstallerResult.Failure(
                    "Windows Installer could not be started.");
            }

            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode switch
            {
                SuccessExitCode =>
                    InstallerResult.Success(),

                SuccessRestartRequiredExitCode =>
                    InstallerResult.Success(
                        restartRequired: true),

                _ =>
                    InstallerResult.Failure(
                        $"Windows Installer exited with code {process.ExitCode}.",
                        process.ExitCode)
            };
        }
        catch (OperationCanceledException)
        {
            return InstallerResult.Failure(
                "Atlas Edge installation was cancelled.");
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode == 1223)
        {
            return InstallerResult.Failure(
                "Administrator approval was cancelled.");
        }
        catch (Exception exception)
        {
            return InstallerResult.Failure(
                exception.Message);
        }
    }
}

public sealed record InstallerResult(
    bool Succeeded,
    bool RestartRequired,
    int? ExitCode,
    string? Error)
{
    public static InstallerResult Success(
        bool restartRequired = false) =>
        new(
            Succeeded: true,
            RestartRequired: restartRequired,
            ExitCode: restartRequired ? 3010 : 0,
            Error: null);

    public static InstallerResult Failure(
        string error,
        int? exitCode = null) =>
        new(
            Succeeded: false,
            RestartRequired: false,
            ExitCode: exitCode,
            Error: error);
}