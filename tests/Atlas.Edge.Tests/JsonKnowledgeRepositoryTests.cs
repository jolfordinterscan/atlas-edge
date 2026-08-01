using Atlas.Edge.Knowledge;

namespace Atlas.Edge.Tests;

public sealed class JsonKnowledgeRepositoryTests
{
    [Fact]
    public async Task AddAndRead_PersistsRecordAcrossRepositoryInstances()
    {
        var context = CreateContext();
        try
        {
            var record = KnowledgeTestData.CreateRecord();
            using (var repository = new JsonKnowledgeRepository(context.RepositoryPath))
            {
                Assert.Equal(KnowledgeAddStatus.Added, await repository.AddAsync(record, CancellationToken.None));
            }

            using var reopened = new JsonKnowledgeRepository(context.RepositoryPath);
            var restored = await reopened.ReadAsync(record.RecordId, CancellationToken.None);

            Assert.NotNull(restored);
            Assert.Equal(record.RecordId, restored.RecordId);
            Assert.Equal(record.Issue, restored.Issue);
            Assert.Equal(record.Scanner, restored.Scanner);
            Assert.Empty(Directory.EnumerateFiles(context.DirectoryPath, "*.tmp"));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(context.RepositoryPath));
            }
        }
        finally
        {
            DeleteContext(context);
        }
    }

    [Fact]
    public async Task Add_SameRecordTwiceIsIdempotent()
    {
        var context = CreateContext();
        try
        {
            var record = KnowledgeTestData.CreateRecord();
            using var repository = new JsonKnowledgeRepository(context.RepositoryPath);

            Assert.Equal(KnowledgeAddStatus.Added, await repository.AddAsync(record, CancellationToken.None));
            Assert.Equal(KnowledgeAddStatus.Duplicate, await repository.AddAsync(record, CancellationToken.None));
            Assert.Single(await repository.SearchAsync(new KnowledgeSearchQuery(), CancellationToken.None));
        }
        finally
        {
            DeleteContext(context);
        }
    }

    [Fact]
    public async Task Add_ConflictingRecordIdThrowsAndPreservesOriginal()
    {
        var context = CreateContext();
        try
        {
            var original = KnowledgeTestData.CreateRecord();
            var conflict = original with
            {
                Issue = new Issue("USB disconnect", "USB connection dropped.", "USB-9")
            };
            using var repository = new JsonKnowledgeRepository(context.RepositoryPath);
            await repository.AddAsync(original, CancellationToken.None);

            var exception = await Assert.ThrowsAsync<KnowledgeRecordConflictException>(() =>
                repository.AddAsync(conflict, CancellationToken.None));

            Assert.Equal(original.RecordId, exception.RecordId);
            Assert.Equal(original.Issue, (await repository.ReadAsync(original.RecordId, CancellationToken.None))!.Issue);
        }
        finally
        {
            DeleteContext(context);
        }
    }

    [Fact]
    public async Task Repository_SerializesConcurrentAddsWithoutLosingRecords()
    {
        var context = CreateContext();
        try
        {
            using var repository = new JsonKnowledgeRepository(context.RepositoryPath);
            var records = Enumerable.Range(0, 20).Select(_ => KnowledgeTestData.CreateRecord()).ToArray();

            await Task.WhenAll(records.Select(record => repository.AddAsync(record, CancellationToken.None)));

            Assert.Equal(20, (await repository.SearchAsync(new KnowledgeSearchQuery(), CancellationToken.None)).Length);
        }
        finally
        {
            DeleteContext(context);
        }
    }

    [Fact]
    public async Task Repository_FailsClosedOnCorruptJson()
    {
        var context = CreateContext();
        try
        {
            Directory.CreateDirectory(context.DirectoryPath);
            await File.WriteAllTextAsync(context.RepositoryPath, "{ not valid json");
            using var repository = new JsonKnowledgeRepository(context.RepositoryPath);

            await Assert.ThrowsAsync<KnowledgeRepositoryException>(() =>
                repository.SearchAsync(new KnowledgeSearchQuery(), CancellationToken.None));
            await Assert.ThrowsAsync<KnowledgeRepositoryException>(() =>
                repository.AddAsync(KnowledgeTestData.CreateRecord(), CancellationToken.None));
            Assert.Equal("{ not valid json", await File.ReadAllTextAsync(context.RepositoryPath));
        }
        finally
        {
            DeleteContext(context);
        }
    }

    [Fact]
    public async Task Read_ReturnsNullForUnknownOrEmptyId()
    {
        var context = CreateContext();
        try
        {
            using var repository = new JsonKnowledgeRepository(context.RepositoryPath);

            Assert.Null(await repository.ReadAsync(Guid.NewGuid(), CancellationToken.None));
            Assert.Null(await repository.ReadAsync(Guid.Empty, CancellationToken.None));
        }
        finally
        {
            DeleteContext(context);
        }
    }

    private static RepositoryContext CreateContext()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"atlas-edge-knowledge-{Guid.NewGuid():N}");
        return new RepositoryContext(directory, Path.Combine(directory, "knowledge.json"));
    }

    private static void DeleteContext(RepositoryContext context)
    {
        if (Directory.Exists(context.DirectoryPath))
        {
            Directory.Delete(context.DirectoryPath, recursive: true);
        }
    }

    private sealed record RepositoryContext(string DirectoryPath, string RepositoryPath);
}
