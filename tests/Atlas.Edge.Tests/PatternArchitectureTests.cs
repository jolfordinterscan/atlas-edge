using Atlas.Edge.Patterns;

namespace Atlas.Edge.Tests;

public sealed class PatternArchitectureTests
{
    private static readonly string[] ForbiddenDependencies =
    [
        "Atlas.Edge.Transport",
        "Atlas.Edge.Queue",
        "Atlas.Edge.Enrollment",
        "Atlas.Edge.Runtime",
        "Atlas.Edge.Knowledge",
        "Atlas.Edge.AI"
    ];

    private static readonly string[] ForbiddenSourceTerms =
    [
        "HttpClient",
        "HttpListener",
        "Kestrel",
        "System.Data",
        "EntityFramework",
        "SqlConnection",
        "Recommendation",
        "Prediction"
    ];

    [Fact]
    public void PatternProject_DependsOnlyOnScannerEvidence()
    {
        var project = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Atlas.Edge.Patterns",
            "Atlas.Edge.Patterns.csproj"));
        var projectReferences = project.Split("<ProjectReference", StringSplitOptions.None).Length - 1;

        Assert.Equal(1, projectReferences);
        Assert.Contains("Atlas.Edge.ScannerEvidence", project, StringComparison.Ordinal);
        Assert.All(ForbiddenDependencies, dependency => Assert.DoesNotContain(dependency, project, StringComparison.Ordinal));
    }

    [Fact]
    public void PatternAssemblyAndSource_HaveNoForbiddenBoundary()
    {
        var references = typeof(PatternEngine).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();
        Assert.All(ForbiddenDependencies, dependency => Assert.DoesNotContain(dependency, references));

        var sourceDirectory = Path.Combine(FindRepositoryRoot(), "src", "Atlas.Edge.Patterns");
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(sourceDirectory, "*.cs").Select(File.ReadAllText));
        Assert.All(ForbiddenSourceTerms, term => Assert.DoesNotContain(term, source, StringComparison.Ordinal));

        var publicFingerprintProperties = typeof(PatternFingerprint).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        Assert.DoesNotContain(publicFingerprintProperties, name =>
            name.Contains("Digest", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
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
