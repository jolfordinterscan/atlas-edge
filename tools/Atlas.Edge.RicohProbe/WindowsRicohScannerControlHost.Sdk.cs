#if RICOH_SDK
using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;
using AxFiScnLib;

namespace Atlas.Edge.RicohProbe;

public sealed class WindowsRicohScannerControlHost : IRicohScannerControlHost
{
    public Task<T> RunAsync<T>(
        Func<IRicohScannerControlSession, T> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => RunMessageLoop(operation, completion, cancellationToken))
        {
            IsBackground = false,
            Name = "Atlas Edge RICOH SDK probe"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void RunMessageLoop<T>(
        Func<IRicohScannerControlSession, T> operation,
        TaskCompletionSource<T> completion,
        CancellationToken cancellationToken)
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            using var form = new HiddenRicohHostForm(ResolveSampleStatePath());
            var invoked = false;
            form.Shown += (_, _) =>
            {
                if (invoked)
                {
                    return;
                }

                invoked = true;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var session = new WindowsRicohScannerControlSession(form.ScannerControl, form.Handle.ToInt32());
                    completion.TrySetResult(operation(session));
                }
                catch (OperationCanceledException exception)
                {
                    completion.TrySetCanceled(exception.CancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(new RicohProbeHostException(MapHostFailure(exception)));
                }
                finally
                {
                    form.BeginInvoke(form.Close);
                }
            };

            Application.Run(form);
            if (!invoked)
            {
                completion.TrySetException(new RicohProbeHostException(RicohProbeError.HiddenHostFailed));
            }
        }
        catch (Exception exception)
        {
            completion.TrySetException(new RicohProbeHostException(MapHostFailure(exception)));
        }
    }

    private static string ResolveSampleStatePath()
    {
        var sdkRoot = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(value => value.Key == "RicohSdkRoot")?.Value;
        if (string.IsNullOrWhiteSpace(sdkRoot))
        {
            throw new RicohProbeHostException(RicohProbeError.SdkUnavailable);
        }

        return Path.Combine(sdkRoot, "Sample", "ScanTest", "VCS 2017", "FormScan.resx");
    }

    private static string MapHostFailure(Exception exception) =>
        exception is RicohProbeHostException hostException
            ? hostException.DiagnosticCode
            : exception is InvalidOperationException or TypeInitializationException or COMException
                ? RicohProbeError.ActiveXCreationFailed
                : RicohProbeError.HiddenHostFailed;

    private sealed class HiddenRicohHostForm : Form
    {
        private const string ApprovedSampleStateSha256 = "2e1f69bd52dc91d3e79692eef83782643821d57ae9690ac4d3c04fcac46f750c";

        public HiddenRicohHostForm(string statePath)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(-32000, -32000);
            Size = new System.Drawing.Size(64, 64);
            Opacity = 0;

            ScannerControl = new AxFiScn();
            ((ISupportInitialize)ScannerControl).BeginInit();
            SuspendLayout();
            ScannerControl.Enabled = true;
            ScannerControl.Location = new System.Drawing.Point(8, 8);
            ScannerControl.Name = "ricohScannerControl";
            ScannerControl.OcxState = LoadOfficialSampleState(statePath);
            ScannerControl.Size = new System.Drawing.Size(48, 48);
            Controls.Add(ScannerControl);
            ((ISupportInitialize)ScannerControl).EndInit();
            ResumeLayout(false);
            _ = Handle;
        }

        public AxFiScn ScannerControl { get; }

        private static AxHost.State LoadOfficialSampleState(string statePath)
        {
            if (!File.Exists(statePath))
            {
                throw new RicohProbeHostException(RicohProbeError.SdkUnavailable);
            }

            using (var stateFile = File.OpenRead(statePath))
            {
                var hash = Convert.ToHexString(SHA256.HashData(stateFile));
                if (!hash.Equals(ApprovedSampleStateSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new RicohProbeHostException(RicohProbeError.ActiveXCreationFailed);
                }
            }

            using var reader = new ResXResourceReader(statePath);
            foreach (DictionaryEntry entry in reader)
            {
                if (entry.Key is string key &&
                    key.Equals("axFiScn1.OcxState", StringComparison.Ordinal) &&
                    entry.Value is AxHost.State state)
                {
                    return state;
                }
            }

            throw new RicohProbeHostException(RicohProbeError.ActiveXCreationFailed);
        }
    }

    private sealed class WindowsRicohScannerControlSession(AxFiScn control, int windowHandle)
        : IRicohScannerControlSession
    {
        public int WindowHandle { get; } = windowHandle;

        public int ErrorCode => control.ErrorCode;

        public IReadOnlyList<string> GetSources()
        {
            var count = control.GetSourceCount();
            var sources = new List<string>(Math.Clamp(count, 0, 64));
            for (var index = 0; index < count && index < 64; index++)
            {
                sources.Add(control.GetSourceName(index));
            }

            return sources;
        }

        public RicohSdkSourceEnumeration EnumerateSources()
        {
            var count = control.GetSourceCount();
            if (count < 1)
            {
                return new RicohSdkSourceEnumeration(count, -1, []);
            }

            var sources = new List<RicohSdkEnumeratedSource>(Math.Min(count, 64));
            for (var index = 0; index < count && index < 64; index++)
            {
                sources.Add(new RicohSdkEnumeratedSource(index, control.GetSourceName(index)));
            }

            var selectedIndex = control.GetSourceSelect();
            return new RicohSdkSourceEnumeration(count, selectedIndex, sources);
        }

        public int SelectSourceName(string sourceName) => control.SelectSourceName(sourceName);

        public int OpenScanner(int containingWindowHandle) => control.OpenScanner(containingWindowHandle);

        public string? GetSerialNumber(int containingWindowHandle) => control.GetSerialNumber(containingWindowHandle);

        public int CloseScanner(int containingWindowHandle) => control.CloseScanner(containingWindowHandle);
    }
}
#endif
