using System.Diagnostics;
using Atlas.Edge.RicohProbe;

internal static class Program
{
    private static readonly TimeSpan WorkerTimeout = TimeSpan.FromSeconds(15);

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        var request = RicohProbeArguments.Parse(args);
        if (request.Operation == RicohProbeOperation.ReadSerial &&
            !args.Contains("--internal-worker", StringComparer.Ordinal))
        {
            return await RunSupervisedWorkerAsync(args).ConfigureAwait(false);
        }

        var availability = new WindowsRicohRuntimeAvailability(
#if RICOH_SDK
            true
#else
            false
#endif
        );

        IRicohScannerControlHost host =
#if RICOH_SDK
            new WindowsRicohScannerControlHost();
#else
            new NoOpRicohScannerControlHost();
#endif

        var probe = new RicohSerialProbe(
            availability,
            host,
            new MachineWideRicohSessionGate(),
            new RicohSerialValidator(),
            TimeProvider.System);

        using var timeout = new CancellationTokenSource(WorkerTimeout);
        var result = await probe.ExecuteAsync(request, timeout.Token).ConfigureAwait(false);
        Console.WriteLine(RicohProbeJson.Serialize(result));
        return result.Status is "Success" or "Available" ? 0 : 2;
    }

    private static async Task<int> RunSupervisedWorkerAsync(string[] args)
    {
        var startInfo = CreateWorkerStartInfo(args);
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(WorkerTimeout);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output))
            {
                return WriteSupervisorFailure(RicohProbeError.UnhandledFailure);
            }

            Console.Write(output);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return WriteSupervisorFailure(RicohProbeError.Timeout);
        }
        catch
        {
            return WriteSupervisorFailure(RicohProbeError.UnhandledFailure);
        }
    }

    private static ProcessStartInfo CreateWorkerStartInfo(IReadOnlyList<string> args)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException();
        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        }

        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("--internal-worker");
        return startInfo;
    }

    private static int WriteSupervisorFailure(string diagnosticCode)
    {
        Console.WriteLine(RicohProbeJson.Serialize(new RicohSerialProbeResult
        {
            Operation = RicohProbeOperation.ReadSerial.ToString(),
            DiagnosticCode = diagnosticCode
        }));
        return 2;
    }
}

public static class RicohProbeArguments
{
    public static RicohProbeRequest Parse(IReadOnlyList<string> args)
    {
        var operation = args.Contains("--check", StringComparer.OrdinalIgnoreCase)
            ? RicohProbeOperation.Check
            : args.Contains("--read-serial", StringComparer.OrdinalIgnoreCase)
                ? RicohProbeOperation.ReadSerial
                : args.Contains("--list-sources", StringComparer.OrdinalIgnoreCase)
                    ? RicohProbeOperation.ListSources
                    : RicohProbeOperation.None;

        return new(
            operation,
            Value(args, "--source-name"),
            Value(args, "--manufacturer"),
            Value(args, "--model"),
            Value(args, "--usb-vid"),
            Value(args, "--usb-pid"));
    }

    private static string? Value(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
