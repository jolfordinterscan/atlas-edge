using System.Collections.Immutable;

namespace Atlas.Edge.Knowledge;

public enum KnowledgeAddStatus
{
    Added,
    Duplicate
}

public sealed record KnowledgeSearchQuery(
    string? Manufacturer = null,
    string? Model = null,
    string? Firmware = null,
    string? Driver = null,
    string? Issue = null,
    string? ErrorCode = null,
    string? Part = null,
    string? Resolution = null);

public interface IKnowledgeRepository
{
    Task<KnowledgeAddStatus> AddAsync(KnowledgeRecord record, CancellationToken cancellationToken);

    Task<KnowledgeRecord?> ReadAsync(Guid recordId, CancellationToken cancellationToken);

    Task<ImmutableArray<KnowledgeRecord>> SearchAsync(
        KnowledgeSearchQuery query,
        CancellationToken cancellationToken);
}

public sealed class KnowledgeRecordConflictException : Exception
{
    public KnowledgeRecordConflictException(Guid recordId)
        : base($"A different knowledge record already exists with record ID {recordId}.")
    {
        RecordId = recordId;
    }

    public Guid RecordId { get; }
}

public sealed class KnowledgeRepositoryException : Exception
{
    public KnowledgeRepositoryException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
