using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Atlas.Edge.Tests;

public sealed class InstallerFoundationTests
{
    private const string OfficialLogoHash = "981c99b7d7b4a4985764bbca42b03998ef906fcac1444e0cbc45b7ba52cb7d0d";
    private readonly string root = FindRepositoryRoot();

    [Fact]
    public void Product_DefinesApprovedMetadataPathsAndStableUpgradeIdentity()
    {
        var product = LoadProduct();
        var package = product.Descendants(Wix("Package")).Single();

        Assert.Equal("Atlas Edge", package.Attribute("Name")?.Value);
        Assert.Equal("InterScan", package.Attribute("Manufacturer")?.Value);
        Assert.Equal("perMachine", package.Attribute("Scope")?.Value);
        Assert.Equal("7A55399F-274D-4AB8-BB45-760F8DA853E4", package.Attribute("UpgradeCode")?.Value);
        Assert.Contains(product.Descendants(Wix("Directory")), element =>
            element.Attribute("Id")?.Value == "INSTALLFOLDER" && element.Attribute("Name")?.Value == "Atlas Edge");
        Assert.Contains(product.Descendants(Wix("StandardDirectory")), element =>
            element.Attribute("Id")?.Value == "ProgramFiles64Folder");
        Assert.Contains(product.Descendants(Wix("StandardDirectory")), element =>
            element.Attribute("Id")?.Value == "CommonAppDataFolder");
    }

    [Fact]
    public void Branding_UsesVerifiedOfficialAssetAndRequiredDimensions()
    {
        var logoPath = Path.Combine(root, "installer", "assets", "interscan-logo.svg");
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(logoPath))).ToLowerInvariant();
        var brandingReadme = Read("installer/assets/README.md");

        Assert.Equal(OfficialLogoHash, actual);
        Assert.Contains("493 x 58", brandingReadme, StringComparison.Ordinal);
        Assert.Contains("493 x 312", brandingReadme, StringComparison.Ordinal);
        Assert.Contains("450 x 150", brandingReadme, StringComparison.Ordinal);
        Assert.Contains("No official InterScan application icon was found", brandingReadme, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_MatchesFoundationUsesAutomaticStartupAndRecovery()
    {
        var product = LoadProduct();
        var service = product.Descendants(Wix("ServiceInstall")).Single();
        var serviceControl = product.Descendants(Wix("ServiceControl")).Single();
        var recovery = service.Elements().Single(element => element.Name.LocalName == "ServiceConfig");

        Assert.Equal("Atlas Edge Runtime", service.Attribute("Name")?.Value);
        Assert.Equal("Atlas Edge Runtime", service.Attribute("DisplayName")?.Value);
        Assert.Equal("Enterprise scanner intelligence and monitoring for InterScan Atlas", service.Attribute("Description")?.Value);
        Assert.Equal("auto", service.Attribute("Start")?.Value);
        Assert.Equal("install", serviceControl.Attribute("Start")?.Value);
        Assert.Equal("both", serviceControl.Attribute("Stop")?.Value);
        Assert.Equal("uninstall", serviceControl.Attribute("Remove")?.Value);
        Assert.Equal("restart", recovery.Attribute("FirstFailureActionType")?.Value);
        Assert.Equal("restart", recovery.Attribute("SecondFailureActionType")?.Value);
        Assert.Equal("restart", recovery.Attribute("ThirdFailureActionType")?.Value);
        Assert.Equal("1", recovery.Attribute("ResetPeriodInDays")?.Value);

        var runtimeOptions = Read("src/Atlas.Edge.Runtime/WindowsServiceOptions.cs");
        Assert.Contains("ServiceName { get; set; } = \"Atlas Edge Runtime\"", runtimeOptions, StringComparison.Ordinal);
        Assert.Contains("SCM image path as a quoted path", Read("installer/README.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void EventLogSource_IsRegisteredForCheckpointServiceName()
    {
        var product = LoadProduct();
        var registryKey = product.Descendants(Wix("RegistryKey")).Single(element =>
            element.Attribute("Key")?.Value.Contains("EventLog", StringComparison.Ordinal) == true);

        Assert.Equal("SYSTEM\\CurrentControlSet\\Services\\EventLog\\Application\\Atlas Edge Runtime", registryKey.Attribute("Key")?.Value);
        Assert.Contains(registryKey.Elements(Wix("RegistryValue")), value =>
            value.Attribute("Name")?.Value == "EventMessageFile" &&
            value.Attribute("Value")?.Value == "[INSTALLFOLDER]Atlas.Edge.Runtime.exe");
    }

    [Fact]
    public void DataDirectories_AreLeastPrivilegeAndRetained()
    {
        var product = LoadProduct();
        var permissions = product.Descendants().Where(element => element.Name.LocalName == "PermissionEx").ToArray();
        var permanentComponents = product.Descendants(Wix("Component"))
            .Where(element => element.Attribute("Permanent")?.Value == "yes")
            .Select(element => element.Attribute("Id")?.Value)
            .ToArray();

        Assert.Contains(permissions, value => value.Attribute("User")?.Value == "SYSTEM");
        Assert.Contains(permissions, value => value.Attribute("User")?.Value == "Administrators");
        Assert.DoesNotContain(permissions, value => value.Attribute("User")?.Value is "Everyone" or "Users");
        Assert.Contains("AtlasEdgeConfigurationData", permanentComponents);
        Assert.Contains("AtlasEdgeIdentityData", permanentComponents);
        Assert.Contains("AtlasEdgeDiagnosticsData", permanentComponents);
        Assert.Contains("retained on upgrade and uninstall", Read("installer/README.md"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpgradeRepairSilentAndDowngradePolicies_AreExplicit()
    {
        var product = LoadProduct();
        var upgrade = product.Descendants(Wix("MajorUpgrade")).Single();
        var readme = Read("installer/README.md");

        Assert.Contains("Downgrades are not supported", upgrade.Attribute("DowngradeErrorMessage")?.Value, StringComparison.Ordinal);
        Assert.Contains("msiexec /i", readme, StringComparison.Ordinal);
        Assert.Contains("/qn", readme, StringComparison.Ordinal);
        Assert.Contains("msiexec /fa", readme, StringComparison.Ordinal);
        Assert.Contains("msiexec /x", readme, StringComparison.Ordinal);
        Assert.Contains("1603", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void InstalledConfiguration_IsLocalSafeAndContainsNoMockOrSecret()
    {
        using var document = JsonDocument.Parse(Read("installer/config/appsettings.json"));
        var options = document.RootElement.GetProperty("AtlasEdge");

        Assert.Equal("Production", options.GetProperty("EnvironmentName").GetString());
        Assert.Equal("Null", options.GetProperty("TransportMode").GetString());
        Assert.Equal("Platform", options.GetProperty("ScannerDiscoveryProvider").GetString());
        Assert.Equal("Platform", options.GetProperty("ScannerHealthProvider").GetString());
        Assert.Equal(string.Empty, options.GetProperty("EnrollmentCode").GetString());
        Assert.False(options.GetProperty("ScannerConnectorsEnabled").GetBoolean());
        Assert.False(options.GetProperty("ScannerEvidenceEnabled").GetBoolean());
        Assert.All(
            new[] { options.GetProperty("IngestionUrl").GetString(), options.GetProperty("EnrollmentUrl").GetString() },
            value => Assert.Contains("staging.atlas.interscan.com", value, StringComparison.Ordinal));

        var installerSources = ReadInstallerText();
        Assert.DoesNotContain("Authorization: Bearer ", installerSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EnrollmentCode\": \"SET_", installerSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DangerousAcceptAnyServerCertificateValidator", installerSources, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_AddsNoNetworkMockAiOrScannerCommandSurface()
    {
        var source = ReadInstallerText();
        var forbidden = new[]
        {
            "FirewallException",
            "HttpListener",
            "Kestrel",
            "ScannerCommand",
            "RemoteControl",
            "Atlas Connect",
            "OpenAI",
            "MockScanner",
            "Mock\""
        };

        Assert.All(forbidden, value => Assert.DoesNotContain(value, source, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildScript_UsesIgnoredArtifactRootAndExternalSigningInputs()
    {
        var script = Read("installer/scripts/build-installer.ps1");
        var ignore = Read(".gitignore");

        Assert.Contains("artifacts/installer", script, StringComparison.Ordinal);
        Assert.Contains("--self-contained true", script, StringComparison.Ordinal);
        Assert.Contains("-r win-x64", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("ATLAS_EDGE_SIGN_CERTIFICATE_PATH", script, StringComparison.Ordinal);
        Assert.DoesNotContain("git ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/artifacts/installer/", ignore, StringComparison.Ordinal);
    }

    [Fact]
    public void Version_IsDerivedAsMsiCompatibleNumericVersion()
    {
        var versionText = Read("VERSION.md");
        var version = System.Text.RegularExpressions.Regex.Match(
            versionText,
            "(?m)^- Repository version:\\s*([0-9]+\\.[0-9]+\\.[0-9]+)").Groups[1].Value;

        Assert.Equal("0.6.0", version);
        Assert.True(System.Version.TryParse(version, out _));
        Assert.Contains("Repository version", Read("installer/scripts/build-installer.ps1"), StringComparison.Ordinal);
    }

    private XDocument LoadProduct() => XDocument.Load(Path.Combine(root, "installer", "Atlas.Edge.Installer", "Product.wxs"));

    private string Read(string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));

    private string ReadInstallerText() => string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(Path.Combine(root, "installer"), "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));

    private static XName Wix(string localName) => XName.Get(localName, "http://wixtoolset.org/schemas/v4/wxs");

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
