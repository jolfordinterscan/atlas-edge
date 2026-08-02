namespace Atlas.Edge.RicohProbe;

public interface IRicohRuntimeAvailability
{
    RicohRuntimeAvailability Inspect();
}

public interface IRicohScannerControlSession
{
    int WindowHandle { get; }

    int ErrorCode { get; }

    IReadOnlyList<string> GetSources();

    string? GetSelectedSource();

    int SelectSourceName(string sourceName);

    int OpenScanner(int windowHandle);

    string? GetSerialNumber(int windowHandle);

    int CloseScanner(int windowHandle);
}

public interface IRicohScannerControlHost
{
    Task<T> RunAsync<T>(
        Func<IRicohScannerControlSession, T> operation,
        CancellationToken cancellationToken);
}

public interface IRicohSessionGate
{
    IDisposable? TryAcquire();
}

public interface IRicohSerialValidator
{
    bool TryValidate(string? value, RicohProbeRequest request, out string? serial);

    string Mask(string serial);
}

public sealed class NoOpRicohScannerControlHost : IRicohScannerControlHost
{
    public Task<T> RunAsync<T>(
        Func<IRicohScannerControlSession, T> operation,
        CancellationToken cancellationToken) =>
        Task.FromException<T>(new RicohProbeHostException(RicohProbeError.SdkUnavailable));
}

public sealed class RicohProbeHostException(string diagnosticCode) : Exception
{
    public string DiagnosticCode { get; } = diagnosticCode;
}
