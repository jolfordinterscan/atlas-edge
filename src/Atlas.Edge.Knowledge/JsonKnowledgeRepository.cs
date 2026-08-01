using System.Collections.Immutable;
using System.Text.Json;

namespace Atlas.Edge.Knowledge;

public sealed class JsonKnowledgeRepository : IKnowledgeRepository, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _repositoryPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public JsonKnowledgeRepository(string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        _repositoryPath = Path.GetFullPath(repositoryPath);
    }

    public async Task<KnowledgeAddStatus> AddAsync(
        KnowledgeRecord record,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        KnowledgeRecordValidator.Validate(record);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            var existing = document.Records.FirstOrDefault(candidate => candidate.RecordId == record.RecordId);
            if (existing is not null)
            {
                if (string.Equals(
                    KnowledgeJsonSerializer.Serialize(existing),
                    KnowledgeJsonSerializer.Serialize(record),
                    StringComparison.Ordinal))
                {
                    return KnowledgeAddStatus.Duplicate;
                }

                throw new KnowledgeRecordConflictException(record.RecordId);
            }

            var updated = document with { Records = document.Records.Add(record) };
            await SaveAsync(updated, cancellationToken);
            return KnowledgeAddStatus.Added;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KnowledgeRecord?> ReadAsync(Guid recordId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (recordId == Guid.Empty)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            return document.Records.FirstOrDefault(record => record.RecordId == recordId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ImmutableArray<KnowledgeRecord>> SearchAsync(
        KnowledgeSearchQuery query,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(query);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            return document.Records.Where(record => Matches(record, query)).ToImmutableArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }

    private async Task<KnowledgeRepositoryDocument> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_repositoryPath))
        {
            return KnowledgeRepositoryDocument.Empty;
        }

        try
        {
            await using var stream = new FileStream(
                _repositoryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            var document = await JsonSerializer.DeserializeAsync<KnowledgeRepositoryDocument>(
                stream,
                KnowledgeJsonSerializer.Options,
                cancellationToken) ?? throw new JsonException("Knowledge repository JSON was empty.");

            ValidateDocument(document);
            return document;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KnowledgeRepositoryException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new KnowledgeRepositoryException("The local knowledge repository could not be read safely.", ex);
        }
    }

    private async Task SaveAsync(KnowledgeRepositoryDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_repositoryPath) ??
            throw new KnowledgeRepositoryException("The local knowledge repository path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_repositoryPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    KnowledgeJsonSerializer.Options,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporaryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporaryPath, _repositoryPath, overwrite: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DeleteTemporaryFile(temporaryPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DeleteTemporaryFile(temporaryPath);
            throw new KnowledgeRepositoryException("The local knowledge repository could not be saved safely.", ex);
        }
    }

    private static void ValidateDocument(KnowledgeRepositoryDocument document)
    {
        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new KnowledgeRepositoryException("The local knowledge repository schema version is unsupported.");
        }

        if (document.Records.IsDefault)
        {
            throw new KnowledgeRepositoryException("The local knowledge repository record collection is invalid.");
        }

        foreach (var record in document.Records)
        {
            KnowledgeRecordValidator.Validate(record);
        }

        if (document.Records.GroupBy(record => record.RecordId).Any(group => group.Count() > 1))
        {
            throw new KnowledgeRepositoryException("The local knowledge repository contains duplicate record IDs.");
        }
    }

    private static bool Matches(KnowledgeRecord record, KnowledgeSearchQuery query) =>
        Contains(record.Scanner.Manufacturer.Name, query.Manufacturer) &&
        Contains(record.Scanner.Model.Name, query.Model) &&
        Contains(record.Firmware?.Version, query.Firmware) &&
        (IsEmpty(query.Driver) ||
            Contains(record.Driver?.Name, query.Driver) ||
            Contains(record.Driver?.Version, query.Driver)) &&
        (IsEmpty(query.Issue) ||
            Contains(record.Issue.Name, query.Issue) ||
            Contains(record.Issue.Description, query.Issue)) &&
        Contains(record.Issue.ErrorCode, query.ErrorCode) &&
        (IsEmpty(query.Part) || record.PartsUsed.Any(part =>
            Contains(part.PartNumber, query.Part) || Contains(part.Name, query.Part))) &&
        Contains(record.Resolution?.Description, query.Resolution);

    private static bool Contains(string? value, string? filter) =>
        IsEmpty(filter) ||
        (!string.IsNullOrWhiteSpace(value) && value.Contains(filter!.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool IsEmpty(string? value) => string.IsNullOrWhiteSpace(value);

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The primary repository exception remains authoritative.
        }
        catch (UnauthorizedAccessException)
        {
            // The primary repository exception remains authoritative.
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record KnowledgeRepositoryDocument(
        int SchemaVersion,
        ImmutableArray<KnowledgeRecord> Records)
    {
        public static KnowledgeRepositoryDocument Empty { get; } =
            new(CurrentSchemaVersion, ImmutableArray<KnowledgeRecord>.Empty);
    }
}
