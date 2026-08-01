using Atlas.Edge.Knowledge;

namespace Atlas.Edge.Tests;

public sealed class KnowledgeSerializationTests
{
    [Fact]
    public void SerializeAndDeserialize_RoundTripsCompleteEngineeringExperience()
    {
        var original = KnowledgeTestData.CreateRecord();

        var json = KnowledgeJsonSerializer.Serialize(original);
        var restored = KnowledgeJsonSerializer.Deserialize(json);

        Assert.Equal(original.RecordId, restored.RecordId);
        Assert.Equal("paper jam", restored.Issue.Name, ignoreCase: true);
        Assert.Equal("JAM-104", restored.Issue.ErrorCode);
        Assert.Equal("Fujitsu", restored.Scanner.Manufacturer.Name);
        Assert.Equal("fi-8170", restored.Scanner.Model.Name);
        Assert.Equal("SERIAL-8170-A", restored.Scanner.Serial!.Value.Value);
        Assert.Equal("2.4.1", restored.Firmware!.Value.Version);
        Assert.Equal("fi Series Driver", restored.Driver!.Name);
        Assert.Equal(96m, restored.Confidence!.Value.Percent);
        Assert.Equal(TimeSpan.FromMinutes(11), restored.RepairTime!.Value.Duration);
        Assert.Single(restored.Observations);
        Assert.Single(restored.Evidence);
        Assert.Single(restored.PartsUsed);
        Assert.Equal(TimeSpan.Zero, restored.Timestamp.Value.Offset);
        Assert.Contains("\"successful\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_RejectsInvalidRecordRatherThanInventingDefaults()
    {
        var json = KnowledgeJsonSerializer.Serialize(KnowledgeTestData.CreateRecord())
            .Replace("\"customer-a\"", "\"\"", StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => KnowledgeJsonSerializer.Deserialize(json));
    }

    [Fact]
    public void ImmutableCollections_DoNotChangeWhenRecordCopyIsExtended()
    {
        var original = KnowledgeTestData.CreateRecord();
        var changed = original with
        {
            Observations = original.Observations.Add(new Observation(
                "Additional caller-supplied observation.",
                new Timestamp(DateTimeOffset.UtcNow)))
        };

        Assert.Single(original.Observations);
        Assert.Equal(2, changed.Observations.Length);
        Assert.DoesNotContain(original.Observations, observation => observation.Description == "Additional caller-supplied observation.");
    }

    [Fact]
    public void Serialize_RejectsOutOfRangeRecordedConfidence()
    {
        var record = KnowledgeTestData.CreateRecord() with
        {
            Confidence = new Confidence(101m, "Invalid caller input")
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => KnowledgeJsonSerializer.Serialize(record));
    }
}
