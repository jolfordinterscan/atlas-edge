using System.Xml.Linq;

namespace Atlas.Edge.Tests;

public sealed class InstallerLegalPrivacyTests
{
    private const string DraftMarker = "DRAFT FOR REVIEW — NOT APPROVED FOR PRODUCTION USE";
    private readonly string root = FindRepositoryRoot();

    [Fact]
    public void LegalSources_ExistAndCarryRequiredDraftMarkerAndVersions()
    {
        foreach (var name in new[]
                 {
                     "ATLAS-EDGE-EULA-DRAFT.md",
                     "ATLAS-EDGE-EULA-DRAFT.rtf",
                     "ATLAS-EDGE-TELEMETRY-PRIVACY-DRAFT.md",
                     "ATLAS-EDGE-TELEMETRY-PRIVACY-DRAFT.rtf"
                 })
        {
            var path = Path.Combine(root, "installer", "legal", name);
            Assert.True(File.Exists(path), $"Required legal draft is missing: {name}");
            var source = File.ReadAllText(path);
            Assert.True(
                source.Contains(DraftMarker, StringComparison.Ordinal) ||
                source.Contains("DRAFT FOR REVIEW \\u8212? NOT APPROVED FOR PRODUCTION USE", StringComparison.Ordinal),
                $"Required draft warning is missing: {name}");
            Assert.Contains("DRAFT-0.1", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LegalRtf_IsBasicBalancedAndSemanticallyAlignedWithMarkdown()
    {
        foreach (var stem in new[] { "ATLAS-EDGE-EULA-DRAFT", "ATLAS-EDGE-TELEMETRY-PRIVACY-DRAFT" })
        {
            var markdown = Read($"installer/legal/{stem}.md");
            var rtf = Read($"installer/legal/{stem}.rtf");
            Assert.StartsWith("{\\rtf1", rtf, StringComparison.Ordinal);
            Assert.Equal(rtf.Count(character => character == '{'), rtf.Count(character => character == '}'));
            Assert.Equal(markdown.Contains("DRAFT-0.1", StringComparison.Ordinal), rtf.Contains("DRAFT-0.1", StringComparison.Ordinal));
            Assert.Contains("DRAFT FOR REVIEW \\u8212? NOT APPROVED FOR PRODUCTION USE", rtf, StringComparison.Ordinal);
        }

        Assert.Contains("I accept the terms of the License Agreement", Read("installer/legal/ATLAS-EDGE-EULA-DRAFT.rtf"), StringComparison.Ordinal);
        Assert.Contains("I understand the operational telemetry and privacy disclosure", Read("installer/legal/ATLAS-EDGE-TELEMETRY-PRIVACY-DRAFT.rtf"), StringComparison.Ordinal);
    }

    [Fact]
    public void CustomUi_OrdersAndBlocksRequiredAcknowledgements()
    {
        var ui = Read("installer/Atlas.Edge.Installer/InstallerUI.wxs");
        Assert.Contains("Value=\"LicenseAgreementDlg\"", ui, StringComparison.Ordinal);
        Assert.Contains("Value=\"AtlasTelemetryPrivacyDlg\"", ui, StringComparison.Ordinal);
        Assert.Contains("Value=\"AtlasAdministratorAuthorizationDlg\"", ui, StringComparison.Ordinal);
        Assert.Contains("Value=\"InstallDirDlg\"", ui, StringComparison.Ordinal);
        Assert.Contains("Property=\"ATLAS_ACCEPT_TELEMETRY\" CheckBoxValue=\"1\"", ui, StringComparison.Ordinal);
        Assert.Contains("EnableCondition=\"ATLAS_ACCEPT_TELEMETRY = 1\"", ui, StringComparison.Ordinal);
        Assert.Contains("Property=\"ATLAS_ADMIN_AUTHORIZED\" CheckBoxValue=\"1\"", ui, StringComparison.Ordinal);
        Assert.Contains("EnableCondition=\"ATLAS_ADMIN_AUTHORIZED = 1\"", ui, StringComparison.Ordinal);
        Assert.Contains("Property=\"ATLAS_ACCEPT_EULA\" Value=\"1\"", ui, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptanceProperties_ArePublicSecureNonSecretFlagsAndRequiredSilently()
    {
        var document = XDocument.Load(Path.Combine(root, "installer", "Atlas.Edge.Installer", "Product.wxs"));
        var package = document.Root!.Element(Wix("Package"))!;
        var names = new[] { "ATLAS_ACCEPT_EULA", "ATLAS_ACCEPT_TELEMETRY", "ATLAS_ADMIN_AUTHORIZED" };

        foreach (var name in names)
        {
            var property = package.Elements(Wix("Property")).Single(element => element.Attribute("Id")?.Value == name);
            Assert.Equal("yes", property.Attribute("Secure")?.Value);
            Assert.Null(property.Attribute("Hidden"));
            Assert.Null(property.Attribute("Value"));
            var launch = package.Elements(Wix("Launch")).Single(element => element.Attribute("Condition")?.Value.Contains(name, StringComparison.Ordinal) == true);
            Assert.Contains($"{name} = 1", launch.Attribute("Condition")?.Value, StringComparison.Ordinal);
            Assert.Contains($"{name}=1", launch.Attribute("Message")?.Value, StringComparison.Ordinal);
        }

        var guide = Read("docs/112-windows-installation-guide.md");
        Assert.Contains("ATLAS_ACCEPT_EULA=1 ATLAS_ACCEPT_TELEMETRY=1 ATLAS_ADMIN_AUTHORIZED=1", guide, StringComparison.Ordinal);
        Assert.Contains("AtlasEdge-rejected.log", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptanceRecord_ContainsOnlyApprovedOperationalFields()
    {
        var product = Read("installer/Atlas.Edge.Installer/Product.wxs");
        var start = product.IndexOf("Id=\"AtlasEdgeInstallerAcceptance\"", StringComparison.Ordinal);
        var end = product.IndexOf("</Component>", start, StringComparison.Ordinal);
        var component = product[start..end];
        var required = new[]
        {
            "InstallerVersion",
            "EulaDocumentVersion",
            "TelemetryDisclosureVersion",
            "AcceptanceTimestampUtc",
            "InstallationMode"
        };
        var forbidden = new[]
        {
            "UserName",
            "Password",
            "Token",
            "EnrollmentCode",
            "DocumentContent",
            "ScannerSerial",
            "HardwareId"
        };

        Assert.All(required, value => Assert.Contains($"Name=\"{value}\"", component, StringComparison.Ordinal));
        Assert.All(forbidden, value => Assert.DoesNotContain(value, component, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Permanent=\"yes\"", component, StringComparison.Ordinal);
        var readme = Read("installer/README.md");
        Assert.Contains("operational deployment", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not legal proof", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompletionAndWelcomeCopy_AreAccurateAndConservative()
    {
        var product = Read("installer/Atlas.Edge.Installer/Product.wxs");
        var localization = Read("installer/Atlas.Edge.Installer/Atlas.Edge.Installer.en-us.wxl");
        var combined = product + localization;

        Assert.Contains("Atlas Edge has been successfully installed", combined, StringComparison.Ordinal);
        Assert.Contains("Atlas enrollment and cloud connectivity are not configured", combined, StringComparison.Ordinal);
        Assert.Contains("Enterprise Scanner Intelligence", combined, StringComparison.Ordinal);
        Assert.Contains("Publisher: InterScan", combined, StringComparison.Ordinal);
        Assert.Contains("Install Atlas Edge to monitor scanner health, discovery, evidence, and operational status on this computer.", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("enrollment complete", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cloud connected", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scanner discovered", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("telemetry delivered", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("predictive monitoring active", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrivacyDisclosure_ContainsCollectedExcludedAndCaveatCategories()
    {
        var disclosure = Read("installer/legal/ATLAS-EDGE-TELEMETRY-PRIVACY-DRAFT.md");
        var required = new[]
        {
            "Atlas Edge is designed to observe operational health, not document content.",
            "Atlas Edge runtime identity and version",
            "Workstation identity",
            "Page counts when a validated adapter supports them",
            "Queue health",
            "Sanitized errors and diagnostic evidence when enabled by policy",
            "Scanned document images",
            "OCR text",
            "Customer document metadata",
            "User passwords",
            "Enrollment codes",
            "Bearer tokens",
            "Private cryptographic keys",
            "Exact telemetry depends on enabled capabilities and customer policy.",
            "Diagnostic collection must be authorized and scoped.",
            "Future capabilities may require updated disclosure and renewed review."
        };

        Assert.All(required, value => Assert.Contains(value, disclosure, StringComparison.Ordinal));
    }

    [Fact]
    public void LegalDrafts_ContainPlaceholdersButNoInventedEntityDetailsOrApprovalClaim()
    {
        var legal = string.Join(Environment.NewLine, Directory.EnumerateFiles(
                Path.Combine(root, "installer", "legal"),
                "*",
                SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText));

        Assert.Contains("COUNSEL REVIEW REQUIRED", legal, StringComparison.Ordinal);
        Assert.DoesNotContain("approved by counsel", legal, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InterScan, Inc.", legal, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LLC", legal, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Street", legal, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("governed by the laws of", legal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installer_DeclaresFutureUpdateDisclosureButImplementsNoUpdater()
    {
        var ui = Read("installer/Atlas.Edge.Installer/InstallerUI.wxs");
        var projectSources = string.Join(Environment.NewLine, Directory.EnumerateFiles(
                Path.Combine(root, "installer", "Atlas.Edge.Installer"),
                "*",
                SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".wxs" or ".wixproj")
            .Select(File.ReadAllText));

        Assert.Contains("Automatic update behavior is not enabled by this installer foundation.", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduledTask", projectSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UpdateService", projectSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DownloadUrl", projectSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UpdateFeed", projectSources, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallerLegalCheckpoint_AddsNoForbiddenCapabilityOrInboundSurface()
    {
        var source = string.Join(Environment.NewLine, new[]
        {
            Read("installer/Atlas.Edge.Installer/Product.wxs"),
            Read("installer/Atlas.Edge.Installer/InstallerUI.wxs"),
            Read("installer/Atlas.Edge.Bootstrapper/Bundle.wxs"),
            Read("installer/config/appsettings.json")
        });
        var forbidden = new[]
        {
            "FirewallException",
            "HttpListener",
            "Kestrel",
            "AtlasConnect",
            "OpenAI",
            "ScannerCommand",
            "RemoteControl",
            "TicketCreation",
            "AcquireImage"
        };

        Assert.All(forbidden, value => Assert.DoesNotContain(value, source, StringComparison.OrdinalIgnoreCase));
    }

    private string Read(string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));

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
