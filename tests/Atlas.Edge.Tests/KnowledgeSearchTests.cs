using Atlas.Edge.Knowledge;

namespace Atlas.Edge.Tests;

public sealed class KnowledgeSearchTests
{
    [Fact]
    public async Task Search_SupportsEverySpecifiedFieldWithoutRanking()
    {
        var context = CreateContext();
        try
        {
            var first = KnowledgeTestData.CreateRecord();
            var second = KnowledgeTestData.CreateRecord() with
            {
                RecordId = Guid.NewGuid(),
                Issue = new Issue("USB disconnect", "Intermittent USB connection loss.", "USB-22"),
                Resolution = new Resolution("Replaced the USB cable."),
                Firmware = new Firmware("1.8.0"),
                Driver = new Driver("Epson Capture Driver", "5.0"),
                Scanner = new Scanner(
                    new Manufacturer("Epson"),
                    new Model("DS-870"),
                    new Serial("EPSON-2")),
                PartsUsed = [new PartUsed("USB-CABLE-3M", "Shielded USB cable", 1)]
            };
            using var repository = new JsonKnowledgeRepository(context.RepositoryPath);
            await repository.AddAsync(first, CancellationToken.None);
            await repository.AddAsync(second, CancellationToken.None);

            await AssertOnlyAsync(repository, first.RecordId, new KnowledgeSearchQuery(Manufacturer: "fuji"));
            await AssertOnlyAsync(repository, first.RecordId, new KnowledgeSearchQuery(Model: "8170"));
            await AssertOnlyAsync(repository, first.RecordId, new KnowledgeSearchQuery(Firmware: "2.4"));
            await AssertOnlyAsync(repository, first.RecordId, new KnowledgeSearchQuery(Driver: "series"));
            await AssertOnlyAsync(repository, first.RecordId, new KnowledgeSearchQuery(Issue: "paper JAM"));
            await AssertOnlyAsync(repository, first.RecordId, new KnowledgeSearchQuery(ErrorCode: "jam-104"));
            await AssertOnlyAsync(repository, first.RecordId, new KnowledgeSearchQuery(Part: "PA03576"));
            await AssertOnlyAsync(repository, first.RecordId, new KnowledgeSearchQuery(Resolution: "transport path"));
            await AssertOnlyAsync(repository, second.RecordId, new KnowledgeSearchQuery(Part: "shielded"));
        }
        finally
        {
            DeleteContext(context);
        }
    }

    [Fact]
    public async Task Search_CombinesFiltersWithAndAndPreservesInsertionOrder()
    {
        var context = CreateContext();
        try
        {
            var first = KnowledgeTestData.CreateRecord();
            var second = KnowledgeTestData.CreateRecord() with { RecordId = Guid.NewGuid() };
            var unrelated = KnowledgeTestData.CreateRecord() with
            {
                RecordId = Guid.NewGuid(),
                Scanner = new Scanner(
                    new Manufacturer("Canon"),
                    new Model("DR-G2140"),
                    new Serial("CANON-3"))
            };
            using var repository = new JsonKnowledgeRepository(context.RepositoryPath);
            await repository.AddAsync(first, CancellationToken.None);
            await repository.AddAsync(second, CancellationToken.None);
            await repository.AddAsync(unrelated, CancellationToken.None);

            var matches = await repository.SearchAsync(
                new KnowledgeSearchQuery(Manufacturer: "FUJITSU", Issue: "jam", Part: "roller"),
                CancellationToken.None);

            Assert.Equal(2, matches.Length);
            Assert.Equal(first.RecordId, matches[0].RecordId);
            Assert.Equal(second.RecordId, matches[1].RecordId);
            Assert.Empty(await repository.SearchAsync(
                new KnowledgeSearchQuery(Manufacturer: "Canon", ErrorCode: "missing"),
                CancellationToken.None));
        }
        finally
        {
            DeleteContext(context);
        }
    }

    [Fact]
    public async Task Search_BlankFiltersReturnAllRecordsWithoutInference()
    {
        var context = CreateContext();
        try
        {
            using var repository = new JsonKnowledgeRepository(context.RepositoryPath);
            await repository.AddAsync(KnowledgeTestData.CreateRecord(), CancellationToken.None);

            var matches = await repository.SearchAsync(
                new KnowledgeSearchQuery(Manufacturer: " ", Issue: null),
                CancellationToken.None);

            Assert.Single(matches);
        }
        finally
        {
            DeleteContext(context);
        }
    }

    private static async Task AssertOnlyAsync(
        IKnowledgeRepository repository,
        Guid expectedId,
        KnowledgeSearchQuery query)
    {
        var match = Assert.Single(await repository.SearchAsync(query, CancellationToken.None));
        Assert.Equal(expectedId, match.RecordId);
    }

    private static RepositoryContext CreateContext()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"atlas-edge-knowledge-search-{Guid.NewGuid():N}");
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
