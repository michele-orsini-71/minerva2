using Minerva.Models;
using Minerva.Storage;
using Minerva.Utilities;

namespace Minerva.IntegrationTests.Storage;

[Collection("Storage")]
[Trait("Category", "Storage")]
public class ChunkRepositoryTests : IAsyncLifetime
{
    private readonly StorageTestFixture _fixture;
    private readonly PostgresCollectionRepository _collRepo;
    private readonly PostgresChunkRepository _chunkRepo;
    private const string CollectionName = "chunk-test-coll";
    private const int Dimension = 4;

    public ChunkRepositoryTests(StorageTestFixture fixture)
    {
        _fixture = fixture;
        _collRepo = new PostgresCollectionRepository(fixture.DataSource);
        _chunkRepo = new PostgresChunkRepository(fixture.DataSource);
    }

    public async Task InitializeAsync()
    {
        await _fixture.CleanupAsync(honorDisableFlag: false);
        await _collRepo.CreateAsync(new Collection(CollectionName, null, "test-model", Dimension));
        await _fixture.SchemaInitializer.EnsureHnswIndexAsync(CollectionName, Dimension);
    }

    public async Task DisposeAsync() => await _fixture.CleanupAsync();

    [Fact]
    public async Task UpsertAndGetContentHash_RoundTrips()
    {
        var chunks = CreateTestChunks("doc-1", 3);
        await _chunkRepo.UpsertChunksAsync(CollectionName, "doc-1", chunks);

        var hash = await _chunkRepo.GetContentHashAsync(CollectionName, "doc-1");
        Assert.Equal(chunks[0].ContentHash, hash);
    }

    [Fact]
    public async Task Upsert_ReplacesExistingChunks()
    {
        var original = CreateTestChunks("doc-replace", 2);
        await _chunkRepo.UpsertChunksAsync(CollectionName, "doc-replace", original);

        var replacement = CreateTestChunks("doc-replace", 3, contentPrefix: "new-");
        await _chunkRepo.UpsertChunksAsync(CollectionName, "doc-replace", replacement);

        var hash = await _chunkRepo.GetContentHashAsync(CollectionName, "doc-replace");
        Assert.Equal(replacement[0].ContentHash, hash);
    }

    [Fact]
    public async Task AdjacencyPointers_AreCorrect()
    {
        var chunks = CreateTestChunks("doc-adj", 3);
        await _chunkRepo.UpsertChunksAsync(CollectionName, "doc-adj", chunks);

        var ids = chunks.Select(c => c.Id).ToList();
        var retrieved = await _chunkRepo.GetAdjacentChunksAsync(ids);

        var byId = retrieved.ToDictionary(c => c.Id);

        // First chunk: no prev, has next
        Assert.Null(byId[ids[0]].PrevChunkId);
        Assert.Equal(ids[1], byId[ids[0]].NextChunkId);

        // Middle chunk: has prev and next
        Assert.Equal(ids[0], byId[ids[1]].PrevChunkId);
        Assert.Equal(ids[2], byId[ids[1]].NextChunkId);

        // Last chunk: has prev, no next
        Assert.Equal(ids[1], byId[ids[2]].PrevChunkId);
        Assert.Null(byId[ids[2]].NextChunkId);
    }

    [Fact]
    public async Task DeleteBySourceId_RemovesChunks()
    {
        var chunks = CreateTestChunks("doc-del", 2);
        await _chunkRepo.UpsertChunksAsync(CollectionName, "doc-del", chunks);

        await _chunkRepo.DeleteBySourceIdAsync(CollectionName, "doc-del");

        var hash = await _chunkRepo.GetContentHashAsync(CollectionName, "doc-del");
        Assert.Null(hash);
    }

    [Fact]
    public async Task VectorSearch_ReturnsResultsOrderedByDistance()
    {
        // Insert chunks with known embeddings
        var close = new ChunkWithEmbedding(
            HashHelper.GenerateChunkId("vec-doc", 0), "vec-doc", CollectionName, 0,
            "close content", HashHelper.ComputeContentHash("close content"),
            [1.0f, 0.0f, 0.0f, 0.0f]);

        var far = new ChunkWithEmbedding(
            HashHelper.GenerateChunkId("vec-doc", 1), "vec-doc", CollectionName, 1,
            "far content", HashHelper.ComputeContentHash("far content"),
            [0.0f, 0.0f, 0.0f, 1.0f]);

        await _chunkRepo.UpsertChunksAsync(CollectionName, "vec-doc", [close, far]);

        // Query near [1,0,0,0]
        var results = await _chunkRepo.VectorSearchAsync(CollectionName, [1.0f, 0.0f, 0.0f, 0.0f], 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("close content", results[0].Content);
    }

    [Fact]
    public async Task FullTextSearch_ReturnsMatchingChunks()
    {
        var chunks = new List<ChunkWithEmbedding>
        {
            new(HashHelper.GenerateChunkId("fts-doc", 0), "fts-doc", CollectionName, 0,
                "PostgreSQL is a relational database",
                HashHelper.ComputeContentHash("PostgreSQL is a relational database"),
                [0.1f, 0.2f, 0.3f, 0.4f]),
            new(HashHelper.GenerateChunkId("fts-doc", 1), "fts-doc", CollectionName, 1,
                "The weather is sunny today",
                HashHelper.ComputeContentHash("The weather is sunny today"),
                [0.5f, 0.6f, 0.7f, 0.8f]),
        };

        await _chunkRepo.UpsertChunksAsync(CollectionName, "fts-doc", chunks);

        var results = await _chunkRepo.FullTextSearchAsync(CollectionName, "relational database", 10);

        Assert.Single(results);
        Assert.Contains("relational database", results[0].Content);
    }

    [Fact]
    public async Task GetContentHash_ReturnsNull_WhenNotFound()
    {
        var hash = await _chunkRepo.GetContentHashAsync(CollectionName, "nonexistent");
        Assert.Null(hash);
    }

    private static List<ChunkWithEmbedding> CreateTestChunks(string sourceId, int count,
        string contentPrefix = "")
    {
        var chunks = new List<ChunkWithEmbedding>();
        for (int i = 0; i < count; i++)
        {
            var content = $"{contentPrefix}chunk {i} content for {sourceId}";
            chunks.Add(new ChunkWithEmbedding(
                HashHelper.GenerateChunkId(sourceId, i),
                sourceId,
                CollectionName,
                i,
                content,
                HashHelper.ComputeContentHash(content),
                RandomEmbedding()));
        }
        return chunks;
    }

    private static float[] RandomEmbedding()
    {
        var rng = Random.Shared;
        var embedding = new float[Dimension];
        for (int i = 0; i < Dimension; i++)
            embedding[i] = (float)rng.NextDouble();

        // L2 normalize
        var norm = MathF.Sqrt(embedding.Sum(x => x * x));
        for (int i = 0; i < Dimension; i++)
            embedding[i] /= norm;

        return embedding;
    }
}
