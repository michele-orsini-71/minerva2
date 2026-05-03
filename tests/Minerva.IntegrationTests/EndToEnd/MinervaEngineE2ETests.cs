using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Minerva.Collections;
using Minerva.Configuration;
using Minerva.Ingestion;
using Minerva.IntegrationTests.Storage;
using Minerva.Models;
using Minerva.Search;
using Minerva.Storage;

namespace Minerva.IntegrationTests.EndToEnd;

[Collection("Storage")]
[Trait("Category", "E2E")]
public class MinervaEngineE2ETests : IAsyncLifetime
{
    private const int EmbeddingDimension = 16;
    private const string CollectionName = "e2e-collection";
    private const string EmbeddingModel = "mock-embedding";

    private readonly StorageTestFixture _fixture;
    private readonly IMinervaEngine _engine;

    public MinervaEngineE2ETests(StorageTestFixture fixture)
    {
        _fixture = fixture;

        var chunking = new ChunkingOptions
        {
            TargetChunkSize = 600,
            ChunkOverlap = 100,
            EnableSummarization = false,
            EnableContextualization = false,
        };

        var mockEmbeddings = new MockEmbeddingGenerator(EmbeddingDimension);
        var loggerFactory = NullLoggerFactory.Instance;

        var chunkRepository = new PostgresChunkRepository(fixture.DataSource);
        var collectionRepository = new PostgresCollectionRepository(fixture.DataSource);

        var embeddingService = new EmbeddingService(
            mockEmbeddings,
            batchSize: 4,
            loggerFactory.CreateLogger<EmbeddingService>());

        var ingestionPipeline = new IngestionPipeline(
            new DocumentChunker(chunking),
            embeddingService,
            summarizer: null,
            contextualizer: null,
            chunkRepository,
            loggerFactory.CreateLogger<IngestionPipeline>());

        var searchPipeline = new SearchPipeline(
            embeddingService,
            new VectorSearch(chunkRepository),
            new FullTextSearch(chunkRepository),
            new ContextExpander(chunkRepository),
            loggerFactory.CreateLogger<SearchPipeline>());

        var collections = new CollectionManager(
            collectionRepository, fixture.SchemaInitializer, EmbeddingModel, mockEmbeddings);

        _engine = new MinervaEngine(
            ingestionPipeline,
            searchPipeline,
            collections,
            chunkRepository,
            EmbeddingModel,
            mockEmbeddings,
            loggerFactory.CreateLogger<MinervaEngine>());
    }

    public Task InitializeAsync() => _fixture.CleanupAsync(honorDisableFlag: false);
    public Task DisposeAsync() => _fixture.CleanupAsync();

    [Fact]
    public async Task IngestSearchDelete_FullCycle()
    {
        var docA = new Document(
            SourceId: "doc-a",
            Title: "PostgreSQL Guide",
            Text: "PostgreSQL is a powerful open-source relational database. " +
                  "It supports ACID transactions, JSON, and full-text search. " +
                  "Extensions like pgvector enable similarity search on embeddings.");

        var docB = new Document(
            SourceId: "doc-b",
            Title: "Weather Report",
            Text: "Today the weather in San Francisco is sunny and mild. " +
                  "Tomorrow will bring fog in the morning and clear afternoon skies. " +
                  "The weekly forecast calls for consistent mild temperatures.");

        // 1. First ingest: both docs are new (collection auto-created).
        var first = await _engine.IngestAsync(CollectionName, AsAsync(docA, docB));
        Assert.Equal(2, first.Added);
        Assert.Equal(0, first.Updated);
        Assert.Equal(0, first.Deleted);
        Assert.Equal(0, first.Unchanged);

        // 2. Re-ingest the same set: both unchanged.
        var second = await _engine.IngestAsync(CollectionName, AsAsync(docA, docB));
        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.Equal(0, second.Deleted);
        Assert.Equal(2, second.Unchanged);

        // 3. Search dominated by docA's content.
        var results = await _engine.SearchAsync(
            "relational database PostgreSQL",
            [CollectionName],
            new SearchOptions(TopK: 5));
        Assert.NotEmpty(results);
        Assert.Equal("doc-a", results[0].SourceId);

        // 4. Re-ingest with only docB: docA must be deleted by the diff.
        var third = await _engine.IngestAsync(CollectionName, AsAsync(docB));
        Assert.Equal(0, third.Added);
        Assert.Equal(0, third.Updated);
        Assert.Equal(1, third.Deleted);
        Assert.Equal(1, third.Unchanged);

        var afterDelete = await _engine.SearchAsync(
            "relational database PostgreSQL",
            [CollectionName],
            new SearchOptions(TopK: 5));
        Assert.DoesNotContain(afterDelete, r => r.SourceId == "doc-a");
    }

    [Fact]
    public async Task SearchAsync_UnknownCollection_Throws()
    {
        await Assert.ThrowsAsync<Exceptions.ConfigurationException>(() =>
            _engine.SearchAsync("anything", ["does-not-exist"]));
    }

    private static async IAsyncEnumerable<Document> AsAsync(params Document[] docs)
    {
        foreach (var d in docs)
        {
            yield return d;
            await Task.Yield();
        }
    }
}
