using Atlas.Edge.Patterns;
using Atlas.Edge.ScannerEvidence;

namespace Atlas.Edge.Tests;

public sealed class PatternFingerprintTests
{
    [Fact]
    public void Fingerprint_IsDeterministicAndUsesStablePatternId()
    {
        var engine = new PatternEngine();
        var snapshot = PatternTestData.Create(volatileValue: "first", timestamp: DateTimeOffset.Parse("2026-08-01T10:00:00Z"));

        var first = engine.Fingerprint(snapshot);
        var second = engine.Fingerprint(snapshot);

        Assert.Equal(first, second);
        Assert.Equal(first.PatternId, second.PatternId);
        Assert.Matches("^PAT-[0-9]{29}$", first.PatternId.Value);
        Assert.DoesNotContain("SHA", first.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("first", first.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fingerprint_IgnoresOrderingTimestampsProvidersGuidsAndRuntimeIdentifiers()
    {
        var engine = new PatternEngine();
        var first = PatternTestData.Create(
            reverseCollectionOrder: false,
            volatileValue: Guid.NewGuid().ToString("D"),
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05Z"));
        var second = PatternTestData.Create(
            reverseCollectionOrder: true,
            volatileValue: Guid.NewGuid().ToString("D"),
            timestamp: DateTimeOffset.Parse("2030-10-09T08:07:06Z"));

        Assert.Equal(engine.Fingerprint(first), engine.Fingerprint(second));
    }

    [Theory]
    [InlineData("2.4", 12000, 4, 2, 1)]
    [InlineData("2.3", 12001, 4, 2, 1)]
    [InlineData("2.3", 12000, 5, 2, 1)]
    [InlineData("2.3", 12000, 4, 3, 1)]
    [InlineData("2.3", 12000, 4, 2, 2)]
    public void Fingerprint_ChangesForSmallMeaningfulEvidenceDifferences(
        string firmware,
        long pages,
        long jams,
        long doubleFeeds,
        long transportErrors)
    {
        var engine = new PatternEngine();
        var baseline = engine.Fingerprint(PatternTestData.Create());
        var changed = engine.Fingerprint(PatternTestData.Create(
            firmware: firmware,
            lifetimePages: pages,
            jams: jams,
            doubleFeeds: doubleFeeds,
            transportErrors: transportErrors));

        Assert.NotEqual(baseline, changed);
        Assert.NotEqual(baseline.PatternId, changed.PatternId);
    }

    [Fact]
    public void Fingerprint_IgnoresUnknownUnsupportedUnavailableAndFailedValues()
    {
        var unknown = PatternTestData.CreateMinimal("Acme", "ScanPro");
        var otherStates = unknown with
        {
            Driver = Atlas.Edge.ScannerEvidence.EvidenceValue<Atlas.Edge.ScannerEvidence.DriverEvidence>.Unsupported(),
            Connection = Atlas.Edge.ScannerEvidence.EvidenceValue<Atlas.Edge.ScannerEvidence.ConnectionEvidence>.Unavailable(),
            Counters = Atlas.Edge.ScannerEvidence.EvidenceValue<Atlas.Edge.ScannerEvidence.CounterEvidence>.Failed()
        };
        var engine = new PatternEngine();

        Assert.Equal(engine.Fingerprint(unknown), engine.Fingerprint(otherStates));
    }

    [Fact]
    public void Fingerprint_IncludesEachMeaningfulEvidenceCategory()
    {
        var engine = new PatternEngine();
        var baseline = engine.Fingerprint(PatternTestData.Create());
        ScannerEvidenceSnapshot[] changedSnapshots =
        [
            PatternTestData.Create(manufacturer: "Other"),
            PatternTestData.Create(model: "Other"),
            PatternTestData.Create(driver: "4.2"),
            PatternTestData.Create(usbPresent: false),
            PatternTestData.Create(serviceState: EvidenceServiceState.Stopped),
            PatternTestData.Create(eventCode: "driver_failure"),
            PatternTestData.Create(logErrorCode: "paper_jam"),
            PatternTestData.Create(networkErrorState: "offline")
        ];

        Assert.All(changedSnapshots, snapshot => Assert.NotEqual(baseline, engine.Fingerprint(snapshot)));
    }

    [Fact]
    public void Fingerprint_RejectsSnapshotsWithoutMeaningfulKnownEvidence()
    {
        var engine = new PatternEngine();

        Assert.Throws<InvalidOperationException>(() => engine.Fingerprint(PatternTestData.UnknownSnapshot()));
    }
}
