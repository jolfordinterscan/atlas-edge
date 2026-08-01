using System.Collections.Immutable;

namespace Atlas.Edge.Patterns;

public readonly record struct PatternId
{
    public PatternId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith("PAT-", StringComparison.Ordinal) ||
            value.Length == 4 ||
            value.AsSpan(4).ContainsAnyExceptInRange('0', '9'))
        {
            throw new ArgumentException("A pattern ID must use the PAT- prefix followed by decimal digits.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public enum PatternMatchLevel
{
    ExactMatch,
    StrongMatch,
    PartialMatch,
    NoMatch
}

public sealed record PatternEvidenceField(string Name, string Value);

public sealed record PatternEvidenceSummary(ImmutableArray<PatternEvidenceField> Fields);

public sealed class PatternFingerprint : IEquatable<PatternFingerprint>
{
    internal PatternFingerprint(
        PatternId patternId,
        string internalDigest,
        PatternEvidenceSummary summary)
    {
        PatternId = patternId;
        InternalDigest = internalDigest;
        Summary = summary;
    }

    public PatternId PatternId { get; }

    public PatternEvidenceSummary Summary { get; }

    internal string InternalDigest { get; }

    public bool Equals(PatternFingerprint? other) =>
        other is not null && string.Equals(InternalDigest, other.InternalDigest, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as PatternFingerprint);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(InternalDigest);

    public override string ToString() => PatternId.Value;
}

public sealed record PatternSimilarity(
    PatternMatchLevel Level,
    int Score,
    ImmutableArray<string> MatchedFields,
    ImmutableArray<string> DifferentFields);

public sealed record PatternMatch(
    PatternId PatternId,
    PatternSimilarity Similarity);

public sealed record PatternOccurrence(
    PatternId PatternId,
    DateTimeOffset ObservedAtUtc);

public sealed record PatternHistory(
    PatternId PatternId,
    DateTimeOffset FirstObservedUtc,
    DateTimeOffset LastObservedUtc,
    long OccurrenceCount,
    ImmutableArray<string> ObservedManufacturers,
    ImmutableArray<string> ObservedModels,
    ImmutableArray<string> ObservedFirmwareVersions,
    ImmutableArray<string> ObservedDrivers);

public sealed record PatternObservation(
    PatternId PatternId,
    PatternEvidenceSummary Evidence,
    PatternOccurrence Occurrence,
    PatternHistory History);
