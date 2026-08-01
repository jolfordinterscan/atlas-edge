using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Atlas.Edge.ScannerEvidence;

namespace Atlas.Edge.Patterns;

public sealed class PatternEngine
{
    private const int StrongMatchMinimumScore = 70;
    private const int StrongMatchMinimumFields = 3;
    private readonly object _historyLock = new();
    private readonly Dictionary<string, MutablePatternHistory> _histories = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public PatternEngine(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public PatternObservation CreatePattern(ScannerEvidenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var fingerprint = Fingerprint(snapshot);
        var observedAtUtc = _timeProvider.GetUtcNow();
        PatternHistory history;

        lock (_historyLock)
        {
            if (!_histories.TryGetValue(fingerprint.InternalDigest, out var mutableHistory))
            {
                mutableHistory = new MutablePatternHistory(fingerprint.PatternId, observedAtUtc);
                _histories.Add(fingerprint.InternalDigest, mutableHistory);
            }

            mutableHistory.Observe(snapshot, observedAtUtc);
            history = mutableHistory.ToImmutable();
        }

        return new PatternObservation(
            fingerprint.PatternId,
            fingerprint.Summary,
            new PatternOccurrence(fingerprint.PatternId, observedAtUtc),
            history);
    }

    public PatternFingerprint Fingerprint(ScannerEvidenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var fields = PatternNormalizer.Normalize(snapshot);
        if (fields.Count == 0)
        {
            throw new InvalidOperationException("A pattern requires at least one known meaningful evidence field.");
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(fields)));
        var internalDigest = Convert.ToHexString(digest);
        var idNumber = new BigInteger(digest.AsSpan(0, 12), isUnsigned: true, isBigEndian: true);
        var patternId = new PatternId($"PAT-{idNumber.ToString("D29", CultureInfo.InvariantCulture)}");
        return new PatternFingerprint(patternId, internalDigest, ToSummary(fields));
    }

    public PatternSimilarity Compare(ScannerEvidenceSnapshot left, ScannerEvidenceSnapshot right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return Compare(PatternNormalizer.Normalize(left), PatternNormalizer.Normalize(right));
    }

    public PatternMatch Match(ScannerEvidenceSnapshot snapshot, PatternFingerprint candidate)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(candidate);
        var similarity = Compare(
            PatternNormalizer.Normalize(snapshot),
            candidate.Summary.Fields.ToImmutableSortedDictionary(
                field => field.Name,
                field => field.Value,
                StringComparer.Ordinal));
        return new PatternMatch(candidate.PatternId, similarity);
    }

    public PatternEvidenceSummary Summarize(ScannerEvidenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ToSummary(PatternNormalizer.Normalize(snapshot));
    }

    public PatternHistory? GetHistory(PatternFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        lock (_historyLock)
        {
            return _histories.TryGetValue(fingerprint.InternalDigest, out var history)
                ? history.ToImmutable()
                : null;
        }
    }

    private static PatternSimilarity Compare(
        ImmutableSortedDictionary<string, string> left,
        ImmutableSortedDictionary<string, string> right)
    {
        var allFields = left.Keys.Union(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var matched = allFields.Where(field =>
                left.TryGetValue(field, out var leftValue) &&
                right.TryGetValue(field, out var rightValue) &&
                string.Equals(leftValue, rightValue, StringComparison.Ordinal))
            .ToImmutableArray();
        var different = allFields.Except(matched, StringComparer.Ordinal).ToImmutableArray();
        var score = allFields.Length == 0 ? 0 : matched.Length * 100 / allFields.Length;
        var level = score switch
        {
            100 => PatternMatchLevel.ExactMatch,
            >= StrongMatchMinimumScore when matched.Length >= StrongMatchMinimumFields => PatternMatchLevel.StrongMatch,
            > 0 => PatternMatchLevel.PartialMatch,
            _ => PatternMatchLevel.NoMatch
        };

        return new PatternSimilarity(level, score, matched, different);
    }

    private static string Canonicalize(ImmutableSortedDictionary<string, string> fields)
    {
        var canonical = new StringBuilder();
        foreach (var field in fields)
        {
            canonical.Append(Encoding.UTF8.GetByteCount(field.Key).ToString(CultureInfo.InvariantCulture));
            canonical.Append(':');
            canonical.Append(field.Key);
            canonical.Append('=');
            canonical.Append(Encoding.UTF8.GetByteCount(field.Value).ToString(CultureInfo.InvariantCulture));
            canonical.Append(':');
            canonical.Append(field.Value);
            canonical.Append('\n');
        }

        return canonical.ToString();
    }

    private static PatternEvidenceSummary ToSummary(ImmutableSortedDictionary<string, string> fields) =>
        new(fields.Select(field => new PatternEvidenceField(field.Key, field.Value)).ToImmutableArray());

    private sealed class MutablePatternHistory
    {
        private readonly SortedSet<string> _drivers = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _firmware = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _manufacturers = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _models = new(StringComparer.Ordinal);

        public MutablePatternHistory(PatternId patternId, DateTimeOffset observedAtUtc)
        {
            PatternId = patternId;
            FirstObservedUtc = observedAtUtc;
            LastObservedUtc = observedAtUtc;
        }

        public PatternId PatternId { get; }

        public DateTimeOffset FirstObservedUtc { get; }

        public DateTimeOffset LastObservedUtc { get; private set; }

        public long OccurrenceCount { get; private set; }

        public void Observe(ScannerEvidenceSnapshot snapshot, DateTimeOffset observedAtUtc)
        {
            LastObservedUtc = observedAtUtc;
            OccurrenceCount++;
            AddKnown(snapshot.Identity, identity => identity.Manufacturer, _manufacturers);
            AddKnown(snapshot.Identity, identity => identity.Model, _models);
            AddKnown(snapshot.Firmware, firmware => firmware.Version, _firmware);
            AddKnown(snapshot.Driver, driver => driver.Version, _drivers);
        }

        public PatternHistory ToImmutable() => new(
            PatternId,
            FirstObservedUtc,
            LastObservedUtc,
            OccurrenceCount,
            _manufacturers.ToImmutableArray(),
            _models.ToImmutableArray(),
            _firmware.ToImmutableArray(),
            _drivers.ToImmutableArray());

        private static void AddKnown<T>(
            EvidenceValue<T> outer,
            Func<T, EvidenceValue<string>> selector,
            SortedSet<string> target)
        {
            if (outer.State != EvidenceValueState.Known)
            {
                return;
            }

            var value = selector(outer.Value);
            if (value.State == EvidenceValueState.Known && !string.IsNullOrWhiteSpace(value.Value))
            {
                target.Add(value.Value.Trim());
            }
        }
    }
}
