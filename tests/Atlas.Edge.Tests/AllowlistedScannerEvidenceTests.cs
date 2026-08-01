using Atlas.Edge.ScannerEvidence;

namespace Atlas.Edge.Tests;

public sealed class AllowlistedScannerEvidenceTests
{
    [Fact]
    public async Task LocalLogProvider_ReturnsMetadataAndStableCodesWithoutRawContent()
    {
        var context = CreateContext();
        try
        {
            Directory.CreateDirectory(context.AllowedDirectory);
            await File.WriteAllTextAsync(
                context.LogFile,
                "sensitive scanner content\nERROR_CODE=USB_RESET\nerror_code=driver_failure details");
            var provider = new AllowlistedLocalLogEvidenceProvider(
                [context.AllowedDirectory],
                [new LocalLogTarget(context.LogFile, "scanner-map-1")],
                maximumFileSizeBytes: 4096,
                maximumReadBytes: 1024);

            var target = Assert.Single((await provider.DiscoverTargetsAsync(CancellationToken.None)).Value);
            var reference = Assert.Single((await provider.ReadLogReferencesAsync(target, CancellationToken.None)).Value);

            Assert.True(reference.Exists.Value);
            Assert.True(reference.SizeBytes.Value > 0);
            Assert.Collection(
                reference.StableErrorCodes,
                code => Assert.Equal("usb_reset", code),
                code => Assert.Equal("driver_failure", code));
            Assert.DoesNotContain("sensitive", reference.ReferenceId, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(target.CorrelationKeys, key => key.Kind == EvidenceCorrelationKind.AdministratorMapping);
        }
        finally
        {
            DeleteContext(context);
        }
    }

    [Fact]
    public void LocalLogProvider_RejectsOutsideTraversalRelativeAndWildcardPaths()
    {
        var context = CreateContext();
        try
        {
            Directory.CreateDirectory(context.AllowedDirectory);
            var outside = Path.Combine(context.Root, "outside.log");
            var traversal = Path.Combine(context.AllowedDirectory, "..", "outside.log");

            Assert.Throws<ArgumentException>(() => new AllowlistedLocalLogEvidenceProvider(
                [context.AllowedDirectory],
                [new LocalLogTarget(outside)],
                1024,
                512));
            Assert.False(EvidenceSafetyPolicy.IsSafeAllowlistedFile(traversal, [context.AllowedDirectory]));
            Assert.False(EvidenceSafetyPolicy.IsSafeAllowlistedDirectory("relative/path"));
            Assert.False(EvidenceSafetyPolicy.IsSafeAllowlistedDirectory(Path.Combine(context.Root, "*")));
            Assert.False(EvidenceSafetyPolicy.IsSafeAllowlistedDirectory(Path.GetPathRoot(context.Root)));
        }
        finally
        {
            DeleteContext(context);
        }
    }

    [Fact]
    public async Task LocalLogProvider_RejectsSymbolicLinkEscape()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var context = CreateContext();
        try
        {
            Directory.CreateDirectory(context.AllowedDirectory);
            var outside = Path.Combine(context.Root, "outside.log");
            await File.WriteAllTextAsync(outside, "ERROR_CODE=outside");
            File.CreateSymbolicLink(context.LogFile, outside);
            var provider = new AllowlistedLocalLogEvidenceProvider(
                [context.AllowedDirectory],
                [new LocalLogTarget(context.LogFile)],
                1024,
                512);
            var target = Assert.Single((await provider.DiscoverTargetsAsync(CancellationToken.None)).Value);

            var result = await provider.ReadLogReferencesAsync(target, CancellationToken.None);

            Assert.Equal(EvidenceValueState.Failed, result.State);
            Assert.Equal(EvidenceErrorCodes.SymbolicLinkNotAllowed, result.ErrorCode);
        }
        finally
        {
            DeleteContext(context);
        }
    }

    [Fact]
    public async Task LocalLogProvider_EnforcesMaximumFileSize()
    {
        var context = CreateContext();
        try
        {
            Directory.CreateDirectory(context.AllowedDirectory);
            await File.WriteAllBytesAsync(context.LogFile, new byte[1025]);
            var provider = new AllowlistedLocalLogEvidenceProvider(
                [context.AllowedDirectory],
                [new LocalLogTarget(context.LogFile)],
                maximumFileSizeBytes: 1024,
                maximumReadBytes: 512);
            var target = Assert.Single((await provider.DiscoverTargetsAsync(CancellationToken.None)).Value);

            var result = await provider.ReadLogReferencesAsync(target, CancellationToken.None);

            Assert.Equal(EvidenceValueState.Failed, result.State);
            Assert.Equal(EvidenceErrorCodes.FileTooLarge, result.ErrorCode);
        }
        finally
        {
            DeleteContext(context);
        }
    }

    [Fact]
    public void SafetyPolicy_EnforcesRegistryAndNetworkAllowlists()
    {
        Assert.True(EvidenceSafetyPolicy.IsSafeRegistryPath(@"HKLM\SOFTWARE\Vendor\Scanner"));
        Assert.False(EvidenceSafetyPolicy.IsSafeRegistryPath(@"HKLM\SOFTWARE"));
        Assert.False(EvidenceSafetyPolicy.IsSafeRegistryPath(@"HKLM\SOFTWARE\*"));
        Assert.True(EvidenceSafetyPolicy.IsSafeNetworkTarget("https://scanner.example/status", false, false));
        Assert.False(EvidenceSafetyPolicy.IsSafeNetworkTarget("http://scanner.example/status", true, false));
        Assert.True(EvidenceSafetyPolicy.IsSafeNetworkTarget("http://localhost/status", true, false));
        Assert.False(EvidenceSafetyPolicy.IsSafeNetworkTarget("snmp://scanner.example", false, false));
        Assert.True(EvidenceSafetyPolicy.IsSafeNetworkTarget("snmp://scanner.example", false, true));
        Assert.False(EvidenceSafetyPolicy.IsSafeNetworkTarget("https://user:password@scanner.example", false, false));
    }

    private static TestContext CreateContext()
    {
        var root = Path.Combine(Path.GetTempPath(), $"atlas-evidence-{Guid.NewGuid():N}");
        var allowed = Path.Combine(root, "allowed");
        return new TestContext(root, allowed, Path.Combine(allowed, "scanner.log"));
    }

    private static void DeleteContext(TestContext context)
    {
        if (Directory.Exists(context.Root))
        {
            Directory.Delete(context.Root, recursive: true);
        }
    }

    private sealed record TestContext(string Root, string AllowedDirectory, string LogFile);
}
