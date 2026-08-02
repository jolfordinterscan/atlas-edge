using System.Text.Json;
using Atlas.Edge.RicohProbe;

namespace Atlas.Edge.Tests;

public sealed class RicohSerialProbeTests
{
    private static readonly RicohProbeRequest ValidRequest = new(
        RicohProbeOperation.ReadSerial,
        Manufacturer: "FUJITSU",
        Model: "fi-8170",
        UsbVendorId: "04C5",
        UsbProductId: "15FF");

    [Fact]
    public async Task Check_IsPassive_AndNeverCreatesHostSession()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Probe.ExecuteAsync(new(RicohProbeOperation.Check));

        Assert.Equal("Available", result.Status);
        Assert.Equal(0, fixture.Host.RunCount);
        Assert.Equal(0, fixture.Session.OpenCount);
    }

    [Fact]
    public async Task NoOperation_IsPassiveAndRequiresExplicitMode()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Probe.ExecuteAsync(new(RicohProbeOperation.None));

        Assert.Equal(RicohProbeError.ExplicitModeRequired, result.DiagnosticCode);
        Assert.Equal(0, fixture.Host.RunCount);
    }

    [Fact]
    public async Task ListSources_UsesEnumerationOnlyAndNeverOpensScanner()
    {
        var fixture = Fixture.Create();
        fixture.Session.Sources = ["InoTec Scamax USB3", "PaperStream IP fi-8170"];
        fixture.Session.SelectedSourceIndex = 1;

        var result = await fixture.Probe.ExecuteAsync(new(RicohProbeOperation.ListSources));

        Assert.Equal("Success", result.Status);
        Assert.Equal(2, result.SourceCount);
        Assert.Equal(1, result.SelectedSourceIndex);
        Assert.Equal("PaperStream IP fi-8170", result.SelectedSource);
        Assert.Equal(["enumerate"], fixture.Session.Actions);
        Assert.Equal(0, fixture.Session.OpenCount);
        Assert.Equal(0, fixture.Session.SerialReadCount);
        Assert.Equal(0, fixture.Session.CloseCount);
        Assert.Equal(0, fixture.Session.SelectCount);
    }

    [Fact]
    public async Task VerboseListSources_ReportsEnvironmentAndUniqueDriverAssociation()
    {
        var environment = new RicohSourceEnvironmentSnapshot(
            true,
            true,
            true,
            [new("TWAIN", "PaperStream IP fi-8170", "FUJITSU", "fi-8170", "2.0.0.9", "FUJITSU", "X64")],
            []);
        var fixture = Fixture.Create(environment: environment);
        fixture.Session.Sources = ["PaperStream IP fi-8170"];

        var result = await fixture.Probe.ExecuteAsync(new(RicohProbeOperation.ListSources, Verbose: true));

        Assert.Same(environment, result.EnvironmentSources);
        var source = Assert.Single(result.SdkSources);
        Assert.True(source.IsSelected);
        Assert.Equal("TwainDataSource", source.SourceType);
        Assert.Equal("2.0.0.9", source.DriverAssociation?.DriverVersion);
        Assert.False(source.SdkErrorCodeAvailable);
        Assert.Null(source.SdkErrorCode);
    }

    [Fact]
    public async Task ListSources_DoesNotCollectEnvironmentWithoutVerbose()
    {
        var catalog = new FakeEnvironmentCatalog(RicohSourceEnvironmentSnapshot.Empty);
        var fixture = Fixture.Create(environmentCatalog: catalog);

        var result = await fixture.Probe.ExecuteAsync(new(RicohProbeOperation.ListSources));

        Assert.Null(result.EnvironmentSources);
        Assert.Equal(0, catalog.InspectCount);
    }

    [Fact]
    public async Task FailedEnumeration_ReturnsStableDiagnosticWithoutInventingSdkError()
    {
        var fixture = Fixture.Create();
        fixture.Session.EnumerationCountResult = -1;
        fixture.Session.SelectedSourceIndex = -1;

        var result = await fixture.Probe.ExecuteAsync(new(RicohProbeOperation.ListSources));

        Assert.Equal(RicohProbeError.SourceEnumerationFailed, result.DiagnosticCode);
        Assert.Equal("Failed", result.Status);
        Assert.False(result.EnumerationErrorCodeAvailable);
        Assert.Empty(result.SdkSources);
    }

    [Theory]
    [InlineData(false, true, true, true, RicohProbeError.NotWindows)]
    [InlineData(true, false, true, true, RicohProbeError.UnsupportedArchitecture)]
    [InlineData(true, true, false, true, RicohProbeError.SdkUnavailable)]
    [InlineData(true, true, true, false, RicohProbeError.SdkUnavailable)]
    public async Task PreflightFailure_DoesNotCreateHost(
        bool windows,
        bool x64,
        bool registered,
        bool sdkEnabled,
        string expected)
    {
        var fixture = Fixture.Create(new(windows, x64, registered, sdkEnabled));

        var result = await fixture.Probe.ExecuteAsync(ValidRequest);

        Assert.Equal(expected, result.DiagnosticCode);
        Assert.Equal(0, fixture.Host.RunCount);
    }

    [Fact]
    public async Task X86Availability_IsSupportedAndReportedInOutput()
    {
        var fixture = Fixture.Create(new(true, false, true, true, "2.3", IsX86: true));

        var result = await fixture.Probe.ExecuteAsync(new(RicohProbeOperation.Check));

        Assert.Equal("Available", result.Status);
        Assert.Equal("X86", result.Architecture);
        Assert.True(result.SdkAvailable);
        Assert.Equal(0, fixture.Host.RunCount);
    }

    [Fact]
    public void ProjectConfiguration_PreservesX64AndAddsExplicitX86SdkBuilds()
    {
        var project = ReadRepositoryFile("tools/Atlas.Edge.RicohProbe/Atlas.Edge.RicohProbe.csproj");

        Assert.Contains("<RicohProbeArchitecture Condition=\"'$(EnableRicohSdk)' == 'true' and '$(RicohProbeArchitecture)' == ''\">x64</RicohProbeArchitecture>", project, StringComparison.Ordinal);
        Assert.Contains("<PlatformTarget Condition=\"'$(EnableRicohSdk)' == 'true'\">$(RicohProbeArchitecture)</PlatformTarget>", project, StringComparison.Ordinal);
        Assert.Contains("<RuntimeIdentifier Condition=\"'$(EnableRicohSdk)' == 'true'\">win-$(RicohProbeArchitecture)</RuntimeIdentifier>", project, StringComparison.Ordinal);
        Assert.Contains("<Prefer32Bit Condition=\"'$(EnableRicohSdk)' == 'true'\">false</Prefer32Bit>", project, StringComparison.Ordinal);
        Assert.Contains("VCS 2017\\x64\\bin\\Release", project, StringComparison.Ordinal);
        Assert.Contains("VCS 2017\\bin\\Release", project, StringComparison.Ordinal);
        Assert.Contains("RicohProbeArchitecture must be explicitly set to x64 or x86", project, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectConfiguration_KeepsSdkFreeBuildCrossPlatformAndOutputsRidsSeparately()
    {
        var project = ReadRepositoryFile("tools/Atlas.Edge.RicohProbe/Atlas.Edge.RicohProbe.csproj");

        Assert.Contains("<TargetFramework Condition=\"'$(EnableRicohSdk)' != 'true'\">net8.0</TargetFramework>", project, StringComparison.Ordinal);
        Assert.Contains("<TargetFramework Condition=\"'$(EnableRicohSdk)' == 'true'\">net8.0-windows</TargetFramework>", project, StringComparison.Ordinal);
        Assert.Contains("win-$(RicohProbeArchitecture)", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<RuntimeIdentifier>win-x86</RuntimeIdentifier>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<RuntimeIdentifier>win-x64</RuntimeIdentifier>", project, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("InoTec", "fi-8170", "04C5", "15FF")]
    [InlineData("Canon", "fi-8170", "04C5", "15FF")]
    [InlineData("FUJITSU", "fi-7160", "04C5", "15FF")]
    [InlineData("FUJITSU", "fi-8170", "FFFF", "15FF")]
    [InlineData("FUJITSU", "fi-8170", "04C5", "FFFF")]
    [InlineData(null, null, null, null)]
    public async Task UnsupportedTarget_IsRejectedBeforeHost(
        string? manufacturer,
        string? model,
        string? vid,
        string? pid)
    {
        var fixture = Fixture.Create();
        var request = new RicohProbeRequest(
            RicohProbeOperation.ReadSerial,
            Manufacturer: manufacturer,
            Model: model,
            UsbVendorId: vid,
            UsbProductId: pid);

        var result = await fixture.Probe.ExecuteAsync(request);

        Assert.Equal(RicohProbeError.SourceUnsupported, result.DiagnosticCode);
        Assert.Equal(0, fixture.Host.RunCount);
    }

    [Fact]
    public async Task NoSources_ReturnsNotFoundWithoutOpening()
    {
        var fixture = Fixture.Create();
        fixture.Session.Sources = [];

        var result = await fixture.Probe.ExecuteAsync(ValidRequest);

        Assert.Equal(RicohProbeError.SourceNotFound, result.DiagnosticCode);
        Assert.Equal(0, fixture.Session.OpenCount);
    }

    [Fact]
    public async Task OneFi8170Source_IsSelectedAndReturnsSerial()
    {
        var fixture = Fixture.Create();
        fixture.Session.Sources = ["PaperStream IP fi-8170"];
        fixture.Session.Serial = "R123456789";

        var result = await fixture.Probe.ExecuteAsync(ValidRequest);

        Assert.Equal("Success", result.Status);
        Assert.Equal("PaperStream IP fi-8170", result.SelectedSource);
        Assert.Equal("R123456789", result.SerialNumber);
        Assert.Equal("******6789", result.MaskedSerialNumber);
        Assert.True(result.ScannerClosed);
    }

    [Fact]
    public async Task MultipleFi8170Sources_AreAmbiguous()
    {
        var fixture = Fixture.Create();
        fixture.Session.Sources = ["fi-8170", "PaperStream IP fi-8170"];

        var result = await fixture.Probe.ExecuteAsync(ValidRequest);

        Assert.Equal(RicohProbeError.SourceAmbiguous, result.DiagnosticCode);
        Assert.Equal(0, fixture.Session.OpenCount);
    }

    [Fact]
    public async Task ExactSupportedSourceOverride_SelectsOnlyExactName()
    {
        var fixture = Fixture.Create();
        fixture.Session.Sources = ["fi-8170", "PaperStream IP fi-8170"];
        var request = ValidRequest with { SourceName = "fi-8170" };

        var result = await fixture.Probe.ExecuteAsync(request);

        Assert.Equal("Success", result.Status);
        Assert.Equal("fi-8170", result.SelectedSource);
    }

    [Theory]
    [InlineData("FI-8170", RicohProbeError.SourceNotFound)]
    [InlineData("Canon MF620C", RicohProbeError.SourceUnsupported)]
    public async Task InvalidSourceOverride_IsRejected(string source, string expected)
    {
        var fixture = Fixture.Create();
        fixture.Session.Sources = ["fi-8170", "Canon MF620C"];
        var request = ValidRequest with { SourceName = source };

        var result = await fixture.Probe.ExecuteAsync(request);

        Assert.Equal(expected, result.DiagnosticCode);
        Assert.Equal(0, fixture.Session.OpenCount);
    }

    [Fact]
    public async Task OpenFailure_CapturesErrorImmediatelyAndDoesNotReadOrClose()
    {
        var fixture = Fixture.Create();
        fixture.Session.OpenResult = -1;
        fixture.Session.CurrentErrorCode = 0x1D;

        var result = await fixture.Probe.ExecuteAsync(ValidRequest);

        Assert.Equal(RicohProbeError.OpenFailed, result.DiagnosticCode);
        Assert.Equal(0x1D, result.OpenErrorCode);
        Assert.Equal(["sources", "select", "select-error", "open", "open-error"], fixture.Session.Actions);
        Assert.Equal(0, fixture.Session.SerialReadCount);
        Assert.Equal(0, fixture.Session.CloseCount);
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(-1, 0x2B)]
    public async Task BusyOpen_IsMappedWithoutRetry(int openResult, int errorCode)
    {
        var fixture = Fixture.Create();
        fixture.Session.OpenResult = openResult;
        fixture.Session.CurrentErrorCode = errorCode;

        var result = await fixture.Probe.ExecuteAsync(ValidRequest);

        Assert.Equal(RicohProbeError.ScannerBusy, result.DiagnosticCode);
        Assert.Equal(1, fixture.Session.OpenCount);
    }

    [Fact]
    public async Task EmptySerial_CapturesErrorAndStillCloses()
    {
        var fixture = Fixture.Create();
        fixture.Session.Serial = "";
        fixture.Session.CurrentErrorCode = 9;

        var result = await fixture.Probe.ExecuteAsync(ValidRequest);

        Assert.Equal(RicohProbeError.SerialEmpty, result.DiagnosticCode);
        Assert.Equal(9, result.SerialErrorCode);
        Assert.True(result.CloseAttempted);
        Assert.True(result.ScannerClosed);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("N/A")]
    [InlineData("000000")]
    [InlineData("USB\\VID_04C5&PID_15FF\\6&ABC&0&2")]
    [InlineData("04C5")]
    [InlineData("15FF")]
    [InlineData("04C515FF")]
    [InlineData("fi-8170")]
    [InlineData("bad\u0001serial")]
    public async Task InvalidSerial_IsNeverPublished(string serial)
    {
        var fixture = Fixture.Create();
        fixture.Session.Serial = serial;

        var result = await fixture.Probe.ExecuteAsync(ValidRequest);

        Assert.Equal(RicohProbeError.SerialInvalid, result.DiagnosticCode);
        Assert.Null(result.SerialNumber);
        Assert.Null(result.MaskedSerialNumber);
    }

    [Fact]
    public async Task OverlengthSerial_IsRejected()
    {
        var fixture = Fixture.Create();
        fixture.Session.Serial = new string('A', 129);

        var result = await fixture.Probe.ExecuteAsync(ValidRequest);

        Assert.Equal(RicohProbeError.SerialInvalid, result.DiagnosticCode);
    }

    [Fact]
    public async Task SerialReadException_StillClosesAndDoesNotExposeException()
    {
        var fixture = Fixture.Create();
        fixture.Session.ThrowOnSerialRead = true;
        fixture.Session.CurrentErrorCode = 88;

        var result = await fixture.Probe.ExecuteAsync(ValidRequest);

        Assert.Equal(RicohProbeError.SerialReadFailed, result.DiagnosticCode);
        Assert.Equal(88, result.SerialErrorCode);
        Assert.Equal(1, fixture.Session.CloseCount);
        Assert.DoesNotContain("vendor-secret", RicohProbeJson.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CloseFailure_IsReportedAndNoSecondOpenOccurs()
    {
        var fixture = Fixture.Create();
        fixture.Session.CloseResult = -1;
        fixture.Session.CurrentErrorCode = 71;

        var result = await fixture.Probe.ExecuteAsync(ValidRequest);

        Assert.Equal(RicohProbeError.CloseFailed, result.DiagnosticCode);
        Assert.False(result.ScannerClosed);
        Assert.Equal(1, fixture.Session.OpenCount);
        Assert.Equal(1, fixture.Session.CloseCount);
    }

    [Fact]
    public async Task SessionGateHeld_FailsImmediatelyWithoutHost()
    {
        var fixture = Fixture.Create();
        fixture.Gate.Available = false;

        var result = await fixture.Probe.ExecuteAsync(ValidRequest);

        Assert.Equal(RicohProbeError.SessionActive, result.DiagnosticCode);
        Assert.Equal(0, fixture.Host.RunCount);
    }

    [Fact]
    public void MachineWideGate_AcquiresWhenFree()
    {
        var gate = NewMachineWideGate();

        using var lease = gate.TryAcquire();

        Assert.NotNull(lease);
    }

    [Fact]
    public void MachineWideGate_SecondAcquisitionFailsWhileLeaseIsHeld()
    {
        var name = NewGateName();
        var firstGate = new MachineWideRicohSessionGate(name);
        var secondGate = new MachineWideRicohSessionGate(name);
        using var lease = firstGate.TryAcquire();

        var secondLease = secondGate.TryAcquire();

        Assert.NotNull(lease);
        Assert.Null(secondLease);
    }

    [Fact]
    public void MachineWideGate_AcquiresAfterLeaseIsDisposed()
    {
        var name = NewGateName();
        var firstGate = new MachineWideRicohSessionGate(name);
        var secondGate = new MachineWideRicohSessionGate(name);
        var lease = firstGate.TryAcquire();
        Assert.NotNull(lease);
        lease.Dispose();

        using var nextLease = secondGate.TryAcquire();

        Assert.NotNull(nextLease);
    }

    [Fact]
    public void MachineWideGate_LeaseDisposalIsIdempotent()
    {
        var gate = NewMachineWideGate();
        var lease = gate.TryAcquire();
        Assert.NotNull(lease);

        lease.Dispose();
        lease.Dispose();

        using var nextLease = gate.TryAcquire();
        Assert.NotNull(nextLease);
    }

    [Fact]
    public void MachineWideGate_LeaseCanBeDisposedFromDifferentThread()
    {
        var gate = NewMachineWideGate();
        var lease = gate.TryAcquire() ?? throw new InvalidOperationException("Gate was not acquired.");
        var acquisitionThread = Environment.CurrentManagedThreadId;
        var disposalThread = acquisitionThread;

        var thread = new Thread(() =>
        {
            disposalThread = Environment.CurrentManagedThreadId;
            lease.Dispose();
        });
        thread.Start();
        thread.Join();

        Assert.NotEqual(acquisitionThread, disposalThread);
        using var nextLease = gate.TryAcquire();
        Assert.NotNull(nextLease);
    }

    [Fact]
    public async Task MachineWideGate_ConcurrentGateInstancesFailFast()
    {
        var name = NewGateName();
        var firstGate = new MachineWideRicohSessionGate(name);
        var secondGate = new MachineWideRicohSessionGate(name);
        using var lease = firstGate.TryAcquire();
        Assert.NotNull(lease);

        var secondLease = await Task.Run(secondGate.TryAcquire);

        Assert.Null(secondLease);
    }

    [Fact]
    public void SerialMask_ShowsOnlyLastFourCharacters()
    {
        var validator = new RicohSerialValidator();

        Assert.Equal("******6789", validator.Mask("R123456789"));
    }

    [Fact]
    public void ResultJson_UsesStableCamelCaseShape()
    {
        var json = RicohProbeJson.Serialize(new RicohSerialProbeResult
        {
            Operation = "ReadSerial",
            SdkAvailable = true,
            SerialAvailable = true,
            SerialNumber = "R123456789",
            MaskedSerialNumber = "******6789",
            Status = "Success"
        });
        using var document = JsonDocument.Parse(json);

        Assert.Equal("1.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("ReadSerial", document.RootElement.GetProperty("operation").GetString());
        Assert.True(document.RootElement.GetProperty("serialAvailable").GetBoolean());
        Assert.Equal("R123456789", document.RootElement.GetProperty("serialNumber").GetString());
    }

    [Fact]
    public void Arguments_RequireExplicitReadAndParseTargetContext()
    {
        var none = RicohProbeArguments.Parse([]);
        var read = RicohProbeArguments.Parse([
            "--read-serial",
            "--manufacturer",
            "FUJITSU",
            "--model",
            "fi-8170",
            "--usb-vid",
            "04C5",
            "--usb-pid",
            "15FF"
        ]);

        Assert.Equal(RicohProbeOperation.None, none.Operation);
        Assert.Equal(RicohProbeOperation.ReadSerial, read.Operation);
        Assert.Equal("fi-8170", read.Model);
    }

    [Fact]
    public void Arguments_ParseVerboseSourceListing()
    {
        var request = RicohProbeArguments.Parse(["--list-sources", "--verbose"]);

        Assert.Equal(RicohProbeOperation.ListSources, request.Operation);
        Assert.True(request.Verbose);
    }

    [Fact]
    public void ProbeContract_ExposesNoAcquisitionOrCommandSurface()
    {
        var forbidden = new[]
        {
            "OpenScanner2", "StartScan", "Transfer", "ShowAcquireImage", "Reset", "EEPROM", "Firmware"
        };
        var exposedNames = typeof(IRicohScannerControlSession).GetMethods().Select(value => value.Name).ToArray();

        foreach (var name in forbidden)
        {
            Assert.DoesNotContain(name, exposedNames);
        }
    }

    private static MachineWideRicohSessionGate NewMachineWideGate() => new(NewGateName());

    private static string NewGateName() => $"InterScan.AtlasEdge.RicohSdk.Tests.{Guid.NewGuid():N}";

    private static string ReadRepositoryFile(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private sealed record Fixture(
        RicohSerialProbe Probe,
        FakeHost Host,
        FakeSession Session,
        FakeGate Gate)
    {
        public static Fixture Create(
            RicohRuntimeAvailability? availability = null,
            RicohSourceEnvironmentSnapshot? environment = null,
            IRicohSourceEnvironmentCatalog? environmentCatalog = null)
        {
            var session = new FakeSession();
            var host = new FakeHost(session);
            var gate = new FakeGate();
            var probe = new RicohSerialProbe(
                new FakeAvailability(availability ?? new(true, true, true, true, "2.3")),
                host,
                gate,
                new RicohSerialValidator(),
                TimeProvider.System,
                environmentCatalog ?? new FakeEnvironmentCatalog(environment ?? RicohSourceEnvironmentSnapshot.Empty));
            return new(probe, host, session, gate);
        }
    }

    private sealed class FakeEnvironmentCatalog(RicohSourceEnvironmentSnapshot snapshot)
        : IRicohSourceEnvironmentCatalog
    {
        public int InspectCount { get; private set; }

        public Task<RicohSourceEnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken)
        {
            InspectCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FakeAvailability(RicohRuntimeAvailability availability) : IRicohRuntimeAvailability
    {
        public RicohRuntimeAvailability Inspect() => availability;
    }

    private sealed class FakeHost(FakeSession session) : IRicohScannerControlHost
    {
        public int RunCount { get; private set; }

        public Task<T> RunAsync<T>(
            Func<IRicohScannerControlSession, T> operation,
            CancellationToken cancellationToken)
        {
            RunCount++;
            return Task.FromResult(operation(session));
        }
    }

    private sealed class FakeGate : IRicohSessionGate
    {
        public bool Available { get; set; } = true;

        public IDisposable? TryAcquire() => Available ? new Lease() : null;

        private sealed class Lease : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeSession : IRicohScannerControlSession
    {
        public IReadOnlyList<string> Sources { get; set; } = ["fi-8170"];
        public int OpenResult { get; set; }
        public int CloseResult { get; set; }
        public int CurrentErrorCode { get; set; }
        public int EnumerationCountResult { get; set; } = int.MinValue;
        public int SelectedSourceIndex { get; set; }
        public string? Serial { get; set; } = "R123456789";
        public bool ThrowOnSerialRead { get; set; }
        public int OpenCount { get; private set; }
        public int CloseCount { get; private set; }
        public int SerialReadCount { get; private set; }
        public int SelectCount { get; private set; }
        public List<string> Actions { get; } = [];

        public int WindowHandle => 42;

        public int ErrorCode
        {
            get
            {
                Actions.Add(Actions.LastOrDefault() switch
                {
                    "select" => "select-error",
                    "open" => "open-error",
                    "serial" => "serial-error",
                    "close" => "close-error",
                    _ => "error"
                });
                return CurrentErrorCode;
            }
        }

        public IReadOnlyList<string> GetSources()
        {
            Actions.Add("sources");
            return Sources;
        }

        public RicohSdkSourceEnumeration EnumerateSources()
        {
            Actions.Add("enumerate");
            var count = EnumerationCountResult == int.MinValue ? Sources.Count : EnumerationCountResult;
            return count < 0
                ? new(count, SelectedSourceIndex, [])
                : new(
                    count,
                    SelectedSourceIndex,
                    Sources.Select((source, index) => new RicohSdkEnumeratedSource(index, source)).ToArray());
        }

        public int SelectSourceName(string sourceName)
        {
            Actions.Add("select");
            SelectCount++;
            return 0;
        }

        public int OpenScanner(int windowHandle)
        {
            Actions.Add("open");
            OpenCount++;
            return OpenResult;
        }

        public string? GetSerialNumber(int windowHandle)
        {
            Actions.Add("serial");
            SerialReadCount++;
            if (ThrowOnSerialRead)
            {
                throw new InvalidOperationException("vendor-secret");
            }

            return Serial;
        }

        public int CloseScanner(int windowHandle)
        {
            Actions.Add("close");
            CloseCount++;
            return CloseResult;
        }
    }
}
