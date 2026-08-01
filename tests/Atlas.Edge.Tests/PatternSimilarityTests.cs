using Atlas.Edge.Patterns;

namespace Atlas.Edge.Tests;

public sealed class PatternSimilarityTests
{
    [Fact]
    public void Compare_ReturnsExactMatchWithAllFieldsExplained()
    {
        var engine = new PatternEngine();
        var left = PatternTestData.Create(volatileValue: "left");
        var right = PatternTestData.Create(volatileValue: "right", reverseCollectionOrder: true);

        var similarity = engine.Compare(left, right);

        Assert.Equal(PatternMatchLevel.ExactMatch, similarity.Level);
        Assert.Equal(100, similarity.Score);
        Assert.NotEmpty(similarity.MatchedFields);
        Assert.Empty(similarity.DifferentFields);
    }

    [Fact]
    public void Compare_ReturnsStrongMatchForOneDifferenceAcrossRichEvidence()
    {
        var engine = new PatternEngine();

        var similarity = engine.Compare(
            PatternTestData.Create(),
            PatternTestData.Create(jams: 5));

        Assert.Equal(PatternMatchLevel.StrongMatch, similarity.Level);
        Assert.InRange(similarity.Score, 70, 99);
        Assert.Contains(similarity.DifferentFields, field => field.Contains("jam_count", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_ReturnsPartialAndNoMatchDeterministically()
    {
        var engine = new PatternEngine();
        var partial = engine.Compare(
            PatternTestData.CreateMinimal("Acme", "ScanPro"),
            PatternTestData.CreateMinimal("Acme", "Other"));
        var none = engine.Compare(
            PatternTestData.CreateMinimal("Acme", "ScanPro"),
            PatternTestData.CreateMinimal("Other", "Different"));

        Assert.Equal(PatternMatchLevel.PartialMatch, partial.Level);
        Assert.Equal(50, partial.Score);
        Assert.Collection(partial.MatchedFields, field => Assert.Equal("identity.manufacturer", field));
        Assert.Equal(PatternMatchLevel.NoMatch, none.Level);
        Assert.Equal(0, none.Score);
        Assert.Empty(none.MatchedFields);
    }

    [Fact]
    public void Match_UsesCandidatePatternIdAndExplainableSimilarity()
    {
        var engine = new PatternEngine();
        var candidate = engine.Fingerprint(PatternTestData.Create());

        var match = engine.Match(PatternTestData.Create(transportErrors: 2), candidate);

        Assert.Equal(candidate.PatternId, match.PatternId);
        Assert.Equal(PatternMatchLevel.StrongMatch, match.Similarity.Level);
        Assert.Contains(match.Similarity.DifferentFields, field => field.Contains("transport_errors", StringComparison.Ordinal));
    }
}
