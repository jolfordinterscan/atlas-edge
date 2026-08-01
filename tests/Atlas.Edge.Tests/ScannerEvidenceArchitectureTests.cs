using Atlas.Edge.ScannerEvidence;

namespace Atlas.Edge.Tests;

public sealed class ScannerEvidenceArchitectureTests
{
    private static readonly string[] ForbiddenDependencies =
    [
        "Atlas.Edge.Transport",
        "Atlas.Edge.Queue",
        "Atlas.Edge.Enrollment",
        "Atlas.Edge.Knowledge"
    ];

    private static readonly string[] ForbiddenSourceTerms =
    [
        "HttpListener",
        "Kestrel",
        "Assembly.Load",
        "LoadFromAssemblyPath",
        "Process.Start",
        "PowerShell",
        "System.Management",
        "ScannerCommand",
        "AcquireImage",
        "StartScan",
        "ConfigureScanner",
        "DangerousAcceptAnyServerCertificateValidator"
    ];

    [Fact]
    public void EvidenceAssembly_HasNoForbiddenProjectDependency()
    {
        var references = typeof(IScannerEvidenceProvider).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();
        Assert.All(ForbiddenDependencies, dependency => Assert.DoesNotContain(dependency, references));

        var project = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Atlas.Edge.ScannerEvidence",
            "Atlas.Edge.ScannerEvidence.csproj"));
        Assert.All(ForbiddenDependencies, dependency => Assert.DoesNotContain(dependency, project, StringComparison.Ordinal));
    }

    [Fact]
    public void EvidenceSource_HasNoListenerPluginShellCommandOrAcquisitionSurface()
    {
        var sourceDirectory = Path.Combine(FindRepositoryRoot(), "src", "Atlas.Edge.ScannerEvidence");
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(sourceDirectory, "*.cs").Select(File.ReadAllText));
        Assert.All(ForbiddenSourceTerms, term => Assert.DoesNotContain(term, source, StringComparison.Ordinal));

        var methods = typeof(IScannerEvidenceProvider).GetMethods().Select(method => method.Name).ToArray();
        Assert.DoesNotContain(methods, method => method.Contains("Command", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methods, method => method.Contains("Acquire", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methods, method => method.StartsWith("Set", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methods, method => method.StartsWith("Write", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Atlas.Edge.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
