using Atlas.Edge.Patterns;

namespace Atlas.Edge.Tests;

public sealed class PatternHistoryTests
{
    [Fact]
    public void CreatePattern_TracksInMemoryHistoryWithTimeProvider()
    {
        var start = DateTimeOffset.Parse("2026-08-01T10:00:00Z");
        var time = new ManualTimeProvider(start);
        var engine = new PatternEngine(time);
        var snapshot = PatternTestData.Create();

        var first = engine.CreatePattern(snapshot);
        time.Advance(TimeSpan.FromHours(2));
        var second = engine.CreatePattern(snapshot);

        Assert.Equal(first.PatternId, second.PatternId);
        Assert.Equal(start, second.History.FirstObservedUtc);
        Assert.Equal(start.AddHours(2), second.History.LastObservedUtc);
        Assert.Equal(2, second.History.OccurrenceCount);
        Assert.Collection(second.History.ObservedManufacturers, value => Assert.Equal("Acme", value));
        Assert.Collection(second.History.ObservedModels, value => Assert.Equal("ScanPro", value));
        Assert.Collection(second.History.ObservedFirmwareVersions, value => Assert.Equal("2.3", value));
        Assert.Collection(second.History.ObservedDrivers, value => Assert.Equal("4.1", value));
        var retrieved = Assert.IsType<PatternHistory>(engine.GetHistory(engine.Fingerprint(snapshot)));
        Assert.Equal(second.History.PatternId, retrieved.PatternId);
        Assert.Equal(second.History.FirstObservedUtc, retrieved.FirstObservedUtc);
        Assert.Equal(second.History.LastObservedUtc, retrieved.LastObservedUtc);
        Assert.Equal(second.History.OccurrenceCount, retrieved.OccurrenceCount);
    }

    [Fact]
    public void CreatePattern_KeepsDifferentFingerprintsInSeparateHistories()
    {
        var engine = new PatternEngine(new ManualTimeProvider(DateTimeOffset.UtcNow));

        var first = engine.CreatePattern(PatternTestData.Create(firmware: "2.3"));
        var second = engine.CreatePattern(PatternTestData.Create(firmware: "2.4"));

        Assert.NotEqual(first.PatternId, second.PatternId);
        Assert.Equal(1, first.History.OccurrenceCount);
        Assert.Equal(1, second.History.OccurrenceCount);
    }

    [Fact]
    public void Summarize_ReturnsImmutableMeaningfulFieldsOnly()
    {
        var summary = new PatternEngine().Summarize(PatternTestData.Create(volatileValue: "secret-runtime-id"));
        var copy = summary.Fields.Add(new PatternEvidenceField("extra", "value"));

        Assert.NotEmpty(summary.Fields);
        Assert.Equal(summary.Fields.Length + 1, copy.Length);
        Assert.Contains(summary.Fields, field => field.Name == "firmware.version" && field.Value == "2.3");
        Assert.Contains(summary.Fields, field => field.Name.Contains("roller_life", StringComparison.Ordinal));
        Assert.DoesNotContain(summary.Fields, field =>
            field.Value.Contains("secret-runtime-id", StringComparison.OrdinalIgnoreCase));
    }
}
