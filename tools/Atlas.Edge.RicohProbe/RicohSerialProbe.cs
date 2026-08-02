using System.Diagnostics;
using System.Globalization;

namespace Atlas.Edge.RicohProbe;

public sealed class RicohSerialProbe(
    IRicohRuntimeAvailability runtimeAvailability,
    IRicohScannerControlHost host,
    IRicohSessionGate sessionGate,
    IRicohSerialValidator serialValidator,
    TimeProvider timeProvider)
{
    private const int Success = 0;
    private const int Failure = -1;
    private const int SequenceError = -3;
    private const int ErrorMaxConnections = 0x2B;

    public async Task<RicohSerialProbeResult> ExecuteAsync(
        RicohProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = timeProvider.GetTimestamp();
        var availability = runtimeAvailability.Inspect();

        if (request.Operation == RicohProbeOperation.None)
        {
            return FailureResult(request, availability, started, RicohProbeError.ExplicitModeRequired);
        }

        if (request.Operation == RicohProbeOperation.ListSources)
        {
            return FailureResult(request, availability, started, RicohProbeError.ListSourcesRequiresRead);
        }

        var preflightError = PreflightError(availability);
        if (preflightError is not null)
        {
            return FailureResult(request, availability, started, preflightError);
        }

        if (request.Operation == RicohProbeOperation.Check)
        {
            return BaseResult(request, availability, started) with
            {
                Status = "Available"
            };
        }

        var targetError = ValidateTarget(request);
        if (targetError is not null)
        {
            return FailureResult(request, availability, started, targetError);
        }

        using var gate = sessionGate.TryAcquire();
        if (gate is null)
        {
            return FailureResult(request, availability, started, RicohProbeError.SessionActive);
        }

        try
        {
            return await host.RunAsync(
                session => ExecuteSession(request, availability, started, session),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return FailureResult(request, availability, started, RicohProbeError.Timeout);
        }
        catch (RicohProbeHostException exception)
        {
            return FailureResult(request, availability, started, exception.DiagnosticCode);
        }
        catch
        {
            return FailureResult(request, availability, started, RicohProbeError.UnhandledFailure);
        }
    }

    private RicohSerialProbeResult ExecuteSession(
        RicohProbeRequest request,
        RicohRuntimeAvailability availability,
        long started,
        IRicohScannerControlSession session)
    {
        IReadOnlyList<string> sources;
        try
        {
            sources = session.GetSources()
                .Select(SanitizeSourceName)
                .Where(value => value is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .Take(64)
                .ToArray();
            _ = SanitizeSourceName(session.GetSelectedSource());
        }
        catch
        {
            return FailureResult(request, availability, started, RicohProbeError.SourceNotFound);
        }

        var sourceSelection = SelectSource(request, sources);
        if (sourceSelection.Error is not null)
        {
            return BaseResult(request, availability, started) with
            {
                SourceCount = sources.Count,
                Sources = sources,
                DiagnosticCode = sourceSelection.Error
            };
        }

        var selectedSource = sourceSelection.Source!;
        int selectResult;
        int selectError;
        try
        {
            selectResult = session.SelectSourceName(selectedSource);
            selectError = session.ErrorCode;
        }
        catch
        {
            return SessionFailure(request, availability, started, sources, selectedSource, RicohProbeError.SourceSelectionFailed);
        }

        if (selectResult != Success)
        {
            return SessionFailure(
                request,
                availability,
                started,
                sources,
                selectedSource,
                RicohProbeError.SourceSelectionFailed,
                serialErrorCode: selectError);
        }

        var openSucceeded = false;
        var openResult = Failure;
        var openError = 0;
        var serialError = 0;
        string? serial = null;
        string? diagnostic = null;
        var closeAttempted = false;
        int? closeResult = null;
        int? closeError = null;

        try
        {
            openResult = session.OpenScanner(session.WindowHandle);
            openError = session.ErrorCode;
            if (openResult != Success)
            {
                diagnostic = openResult == SequenceError || openError == ErrorMaxConnections
                    ? RicohProbeError.ScannerBusy
                    : RicohProbeError.OpenFailed;
            }
            else
            {
                openSucceeded = true;
                try
                {
                    var candidate = session.GetSerialNumber(session.WindowHandle);
                    serialError = session.ErrorCode;
                    if (string.IsNullOrWhiteSpace(candidate))
                    {
                        diagnostic = RicohProbeError.SerialEmpty;
                    }
                    else if (!serialValidator.TryValidate(candidate, request, out serial))
                    {
                        diagnostic = RicohProbeError.SerialInvalid;
                    }
                }
                catch
                {
                    serialError = ReadErrorCodeSafely(session);
                    diagnostic = RicohProbeError.SerialReadFailed;
                }
            }
        }
        catch
        {
            openError = ReadErrorCodeSafely(session);
            diagnostic = RicohProbeError.OpenFailed;
        }
        finally
        {
            if (openSucceeded)
            {
                closeAttempted = true;
                try
                {
                    closeResult = session.CloseScanner(session.WindowHandle);
                    closeError = session.ErrorCode;
                    if (closeResult != Success)
                    {
                        diagnostic = RicohProbeError.CloseFailed;
                    }
                }
                catch
                {
                    closeError = ReadErrorCodeSafely(session);
                    diagnostic = RicohProbeError.CloseFailed;
                }
            }
        }

        var serialAvailable = serial is not null;
        var scannerClosed = closeAttempted && closeResult == Success;
        return BaseResult(request, availability, started) with
        {
            SourceCount = sources.Count,
            Sources = sources,
            SelectedSource = selectedSource,
            OpenResult = openResult,
            OpenErrorCode = openError,
            SerialAvailable = serialAvailable,
            SerialNumber = serial,
            MaskedSerialNumber = serial is null ? null : serialValidator.Mask(serial),
            SerialSource = serial is null ? null : "RicohScannerControlSdk",
            SerialErrorCode = serialError,
            CloseAttempted = closeAttempted,
            CloseResult = closeResult,
            CloseErrorCode = closeError,
            ScannerClosed = scannerClosed,
            Status = diagnostic is null && serialAvailable && scannerClosed ? "Success" : "Failed",
            DiagnosticCode = diagnostic
        };
    }

    private static (string? Source, string? Error) SelectSource(
        RicohProbeRequest request,
        IReadOnlyList<string> sources)
    {
        if (!string.IsNullOrWhiteSpace(request.SourceName))
        {
            var exact = sources.Where(value => value.Equals(request.SourceName, StringComparison.Ordinal)).ToArray();
            if (exact.Length != 1)
            {
                return (null, RicohProbeError.SourceNotFound);
            }

            return IsFi8170Source(exact[0])
                ? (exact[0], null)
                : (null, RicohProbeError.SourceUnsupported);
        }

        var supported = sources.Where(IsFi8170Source).ToArray();
        return supported.Length switch
        {
            0 => (null, RicohProbeError.SourceNotFound),
            1 => (supported[0], null),
            _ => (null, RicohProbeError.SourceAmbiguous)
        };
    }

    private static bool IsFi8170Source(string source)
    {
        var normalized = string.Concat(source.Where(char.IsLetterOrDigit));
        return normalized.Contains("fi8170", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ValidateTarget(RicohProbeRequest request)
    {
        if (!string.Equals(request.Manufacturer, "FUJITSU", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Manufacturer, "RICOH", StringComparison.OrdinalIgnoreCase))
        {
            return RicohProbeError.SourceUnsupported;
        }

        return !string.Equals(request.Model, "fi-8170", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.UsbVendorId, "04C5", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.UsbProductId, "15FF", StringComparison.OrdinalIgnoreCase)
                ? RicohProbeError.SourceUnsupported
                : null;
    }

    private static string? PreflightError(RicohRuntimeAvailability availability)
    {
        if (!availability.IsWindows)
        {
            return RicohProbeError.NotWindows;
        }

        if (!availability.IsX64)
        {
            return RicohProbeError.NotX64;
        }

        return !availability.IsRuntimeRegistered || !availability.IsSdkBuildEnabled
            ? RicohProbeError.SdkUnavailable
            : null;
    }

    private RicohSerialProbeResult FailureResult(
        RicohProbeRequest request,
        RicohRuntimeAvailability availability,
        long started,
        string diagnostic) =>
        BaseResult(request, availability, started) with { DiagnosticCode = diagnostic };

    private RicohSerialProbeResult SessionFailure(
        RicohProbeRequest request,
        RicohRuntimeAvailability availability,
        long started,
        IReadOnlyList<string> sources,
        string selectedSource,
        string diagnostic,
        int? serialErrorCode = null) =>
        BaseResult(request, availability, started) with
        {
            SourceCount = sources.Count,
            Sources = sources,
            SelectedSource = selectedSource,
            SerialErrorCode = serialErrorCode,
            DiagnosticCode = diagnostic
        };

    private RicohSerialProbeResult BaseResult(
        RicohProbeRequest request,
        RicohRuntimeAvailability availability,
        long started) =>
        new()
        {
            Operation = request.Operation.ToString(),
            SdkAvailable = availability.IsWindows && availability.IsX64 &&
                availability.IsRuntimeRegistered && availability.IsSdkBuildEnabled,
            Architecture = availability.IsX64 ? "X64" : RuntimeInformationArchitecture(),
            RuntimeVersion = availability.RuntimeVersion,
            DurationMs = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds
        };

    private static int ReadErrorCodeSafely(IRicohScannerControlSession session)
    {
        try
        {
            return session.ErrorCode;
        }
        catch
        {
            return 0;
        }
    }

    private static string? SanitizeSourceName(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var value = new string(source.Trim().Where(value => !char.IsControl(value)).Take(128).ToArray());
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string RuntimeInformationArchitecture() =>
        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToUpper(CultureInfo.InvariantCulture);
}
