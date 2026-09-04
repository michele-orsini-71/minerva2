using Minerva.Models;
using Minerva.Storage;
using Minerva.IntegrationTests.TestSupport;

namespace Minerva.IntegrationTests.Storage;

[Collection("Storage")]
[Trait("Category", "Storage")]
public class CollectionRepositoryTests : IAsyncLifetime
{
    private readonly StorageTestFixture _fixture;
    private readonly PostgresCollectionRepository _repo;

    public CollectionRepositoryTests(StorageTestFixture fixture)
    {
        _fixture = fixture;
        _repo = new PostgresCollectionRepository(fixture.DataSource);
    }

    public async Task InitializeAsync() => await _fixture.CleanupAsync(honorDisableFlag: false);
    public async Task DisposeAsync() => await _fixture.CleanupAsync();

    [Fact]
    public async Task CreateAndGet_RoundTripsFullProvenance()
    {
        var provenance = TestProvenance.Create(
            embeddingModel: "text-embedding-3-small",
            embeddingDimension: 1536,
            chunkerType: ChunkerType.Custom,
            targetChunkSize: 512,
            chunkOverlap: 64,
            maxSegmentChars: 8000,
            contextualizationEnabled: true,
            contextualizationModel: "qwen2.5",
            summarizerPromptVersion: "1",
            contextualizerPromptVersion: "1",
            ingestorVersion: "0.1.0+abc1234",
            schemaVersion: "003_collection_provenance");
        var collection = new Collection("test-coll", "A test collection", provenance, TestProvenance.Client());

        await _repo.CreateAsync(collection);
        var retrieved = await _repo.GetAsync("test-coll");

        Assert.NotNull(retrieved);
        Assert.Equal("test-coll", retrieved.Name);
        Assert.Equal("A test collection", retrieved.Description);

        var inv = retrieved.Provenance.Invariants;
        Assert.Equal("text-embedding-3-small", inv.EmbeddingModel);
        Assert.Equal(1536, inv.EmbeddingDimension);
        Assert.Equal(ChunkerType.Custom, inv.ChunkerType);
        Assert.Equal(512, inv.TargetChunkSize);
        Assert.Equal(64, inv.ChunkOverlap);
        Assert.Equal(8000, inv.MaxSegmentChars);
        Assert.True(inv.ContextualizationEnabled);
        Assert.Equal("qwen2.5", inv.ContextualizationModel);
        Assert.Equal("1", inv.SummarizerPromptVersion);
        Assert.Equal("1", inv.ContextualizerPromptVersion);

        Assert.Equal("0.1.0+abc1234", retrieved.Provenance.LastRun.IngestorVersion);
        Assert.Equal("003_collection_provenance", retrieved.Provenance.LastRun.SchemaVersion);

        // Convenience accessors hydrate from the invariants.
        Assert.Equal("text-embedding-3-small", retrieved.EmbeddingModel);
        Assert.Equal(1536, retrieved.EmbeddingDimension);
    }

    [Fact]
    public async Task ContextualizationDisabled_OmitsOptionalFields()
    {
        var provenance = TestProvenance.Create(
            embeddingModel: "bge-m3", embeddingDimension: 1024,
            contextualizationEnabled: false);
        await _repo.CreateAsync(new Collection("no-ctx", null, provenance, TestProvenance.Client()));

        var retrieved = await _repo.GetAsync("no-ctx");

        Assert.NotNull(retrieved);
        var inv = retrieved.Provenance.Invariants;
        Assert.False(inv.ContextualizationEnabled);
        Assert.Null(inv.ContextualizationModel);
        Assert.Null(inv.SummarizerPromptVersion);
        Assert.Null(inv.ContextualizerPromptVersion);
    }

    [Fact]
    public async Task Get_ReturnsNull_WhenNotFound()
    {
        var result = await _repo.GetAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task List_ReturnsAllCollections()
    {
        await _repo.CreateAsync(new Collection("coll-a", null,
            TestProvenance.Create(embeddingModel: "model-a", embeddingDimension: 768), TestProvenance.Client()));
        await _repo.CreateAsync(new Collection("coll-b", null,
            TestProvenance.Create(embeddingModel: "model-b", embeddingDimension: 1536), TestProvenance.Client()));

        var list = await _repo.ListAsync();

        Assert.True(list.Count >= 2);
        Assert.Contains(list, c => c.Name == "coll-a");
        Assert.Contains(list, c => c.Name == "coll-b");
    }

    [Fact]
    public async Task Update_ChangesFields()
    {
        await _repo.CreateAsync(new Collection("update-coll", "old desc",
            TestProvenance.Create(embeddingModel: "model", embeddingDimension: 768), TestProvenance.Client()));

        var updated = new Collection("update-coll", "new desc",
            TestProvenance.Create(embeddingModel: "model-v2", embeddingDimension: 1024), TestProvenance.Client());
        await _repo.UpdateAsync(updated);

        var retrieved = await _repo.GetAsync("update-coll");
        Assert.NotNull(retrieved);
        Assert.Equal("new desc", retrieved.Description);
        Assert.Equal("model-v2", retrieved.EmbeddingModel);
        Assert.Equal(1024, retrieved.EmbeddingDimension);
    }

    [Fact]
    public async Task Delete_RemovesCollection()
    {
        await _repo.CreateAsync(new Collection("delete-coll", null,
            TestProvenance.Create(embeddingModel: "model", embeddingDimension: 768), TestProvenance.Client()));
        await _repo.DeleteAsync("delete-coll");

        var result = await _repo.GetAsync("delete-coll");
        Assert.Null(result);
    }

    [Fact]
    public async Task ClientSection_RoundTrips_AsJsonb()
    {
        var client = new ClientProvenance(
            "markdown-indexer",
            new Dictionary<string, object> { ["sourceRoot"] = "/home/user/vault" });
        var collection = new Collection("client-coll", null,
            TestProvenance.Create(), client);

        await _repo.CreateAsync(collection);
        var retrieved = await _repo.GetAsync("client-coll");

        Assert.NotNull(retrieved);
        Assert.Equal("markdown-indexer", retrieved.ClientProvenance.kind);
        Assert.Equal("/home/user/vault", retrieved.ClientProvenance.data["sourceRoot"].ToString());
    }
}
