using Atlas.Edge.ScannerConnectors;

namespace Atlas.Edge.Tests;

public sealed class ScannerConnectorArchitectureTests
{
    private static readonly string[] ForbiddenAssemblyDependencies =
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
        "IEventQueue",
        "IEventTransport",
        "ScannerCommand",
        "AcquireImage",
        "StartScan",
        "ConfigureScanner",
        "Assembly.Load",
        "LoadFromAssemblyPath"
    ];

    [Fact]
    public void ConnectorAssembly_HasNoForbiddenProjectDependency()
    {
        var references = typeof(IScannerConnector).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.All(ForbiddenAssemblyDependencies, dependency => Assert.DoesNotContain(dependency, references));

        var projectFile = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Atlas.Edge.ScannerConnectors",
            "Atlas.Edge.ScannerConnectors.csproj"));
        Assert.DoesNotContain("Atlas.Edge.Transport", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("Atlas.Edge.Queue", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("Atlas.Edge.Enrollment", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("Atlas.Edge.Knowledge", projectFile, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectorSource_HasNoListenerPluginOrCommandSurface()
    {
        var sourceDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Atlas.Edge.ScannerConnectors");
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(sourceDirectory, "*.cs").Select(File.ReadAllText));

        Assert.All(ForbiddenSourceTerms, term => Assert.DoesNotContain(term, source, StringComparison.Ordinal));

        var publicMethods = typeof(IScannerConnector).GetMethods().Select(method => method.Name).ToArray();
        Assert.DoesNotContain(publicMethods, method => method.Contains("Command", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publicMethods, method => method.Contains("Acquire", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publicMethods, method => method.Contains("Configure", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publicMethods, method => method.StartsWith("Set", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publicMethods, method => method.StartsWith("Write", StringComparison.OrdinalIgnoreCase));
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
