using System.Collections.Immutable;

namespace Atlas.Edge.Knowledge;

public readonly record struct Manufacturer(string Name);

public readonly record struct Model(string Name);

public readonly record struct Serial(string Value);

public readonly record struct Firmware(string Version);

public sealed record Driver(string Name, string? Version);

public sealed record Scanner(
    Manufacturer Manufacturer,
    Model Model,
    Serial? Serial);

public sealed record Customer(string CustomerId, string? Name);

public sealed record Site(string SiteId, string? Name);

public readonly record struct Timestamp
{
    public Timestamp(DateTimeOffset value)
    {
        Value = value.ToUniversalTime();
    }

    public DateTimeOffset Value { get; }
}

public sealed record Issue(
    string Name,
    string Description,
    string? ErrorCode);

public sealed record Observation(
    string Description,
    Timestamp ObservedAt);

public sealed record Evidence(
    string Kind,
    string Description,
    string? SourceReference);

public sealed record Resolution(string Description);

public enum OutcomeStatus
{
    Unknown,
    Successful,
    Unsuccessful,
    Partial
}

public sealed record Outcome(
    OutcomeStatus Status,
    string? Notes);

public readonly record struct Confidence(
    decimal Percent,
    string? Basis);

public readonly record struct RepairTime(TimeSpan Duration);

public sealed record PartUsed(
    string PartNumber,
    string Name,
    int Quantity);

public sealed record KnowledgeRecord(
    Guid RecordId,
    Issue Issue,
    ImmutableArray<Observation> Observations,
    ImmutableArray<Evidence> Evidence,
    Resolution? Resolution,
    Outcome? Outcome,
    Confidence? Confidence,
    RepairTime? RepairTime,
    ImmutableArray<PartUsed> PartsUsed,
    Firmware? Firmware,
    Driver? Driver,
    Scanner Scanner,
    Customer Customer,
    Site Site,
    Timestamp Timestamp);
