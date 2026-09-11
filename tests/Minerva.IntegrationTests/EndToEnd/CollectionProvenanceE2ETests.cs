using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Collections;
using Minerva.Exceptions;
using Minerva.Ingestion;
using Minerva.IntegrationTests.Storage;
using Minerva.IntegrationTests.TestSupport;
using Minerva.Models;
using Minerva.Storage;

namespace Minerva.IntegrationTests.EndToEnd;

[Collection("Storage")]
[Trait("Category", "E2E")]
public class CollectionProvenanceE2ETests : IAsyncLifetime
{
    private const int EmbeddingDimension = 16;
    private const string EmbeddingModel = "mock-embedding";
    private const string CollectionName = "provenance-e2e";

    private readonly StorageTestFixture _fixture;
    private readonly PostgresCollectionRepository _collections;

    public CollectionProvenanceE2ETests(StorageTestFixture fixture)
    {
        _fixture = fixture;
        _collections = new PostgresCollectionRepository(fixture.DataSource);
    }

    public Task InitializeAsync() => _fixture.CleanupAsync(honorDisableFlag: false);
    public Task DisposeAsync() => _fixture.CleanupAsync();

    // Slice B verify: a fresh ingest writes the full invariant + last-run set.
    [Fact]
    public async Task Ingest_PopulatesProvenance()
    {
        var chunking = Chunking(targetChunkSize: 512);
        var engine = await BuildEngineAsync(chunking);

        await engine.IngestAsync(CollectionName, TestProvenance.Client(), OneDoc());

        var collection = await _collections.GetAsync(CollectionName);
        Assert.NotNull(collection);

        var inv = collection.Provenance.Invariants;
        Assert.Equal(EmbeddingModel, inv.EmbeddingModel);
        Assert.Equal(EmbeddingDimension, inv.EmbeddingDimension);
        Assert.Equal(ChunkerType.Custom, inv.ChunkerType);
        Assert.Equal(512, inv.TargetChunkSize);
        Assert.Equal(chunking.ChunkOverlap, inv.ChunkOverlap);

        var schemaVersion = await _fixture.SchemaInitializer.GetCurrentSchemaVersionAsync();
        Assert.Equal(schemaVersion, collection.Provenance.LastRun.SchemaVersion);
        Assert.False(string.IsNullOrWhiteSpace(collection.Provenance.LastRun.IngestorVersion));
    }

    // Slice C verify: a drifted invariant is a hard error naming the field.
    [Fact]
    public async Task Reingest_ChangedChunkSize_Throws()
    {
        var first = await BuildEngineAsync(Chunking(targetChunkSize: 512));
        await first.IngestAsync(CollectionName, TestProvenance.Client(), OneDoc());

        var second = await BuildEngineAsync(Chunking(targetChunkSize: 1024));

        var ex = await Assert.ThrowsAsync<CollectionConfigMismatchException>(() =>
            second.IngestAsync(CollectionName, TestProvenance.Client(), OneDoc()));
        Assert.Contains(ex.Drifts, d => d.Field == "targetChunkSize");
    }

    // Slice C verify: the override flag drops and recreates with the new config.
    [Fact]
    public async Task Reingest_ChangedChunkSize_WithOverride_Recreates()
    {
        var first = await BuildEngineAsync(Chunking(targetChunkSize: 512));
        await first.IngestAsync(CollectionName, TestProvenance.Client(), OneDoc());

        var second = await BuildEngineAsync(Chunking(targetChunkSize: 1024));
        await second.IngestAsync(CollectionName, TestProvenance.Client(), OneDoc(), allowRecreateOnConfigMismatch: true);

        var collection = await _collections.GetAsync(CollectionName);
        Assert.NotNull(collection);
        Assert.Equal(1024, collection.Provenance.Invariants.TargetChunkSize);
    }

    private async Task<IIngestEngine> BuildEngineAsync(ChunkingOptions chunking)
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var mockEmbeddings = new MockEmbeddingGenerator(EmbeddingDimension);
        var chunkRepository = new PostgresChunkRepository(_fixture.DataSource);
        var collectionRepository = new PostgresCollectionRepository(_fixture.DataSource);

        var embeddingService = new EmbeddingService(
            mockEmbeddings, batchSize: 4, loggerFactory.CreateLogger<EmbeddingService>());

        var ingestionPipeline = new IngestionPipeline(
            new DocumentChunker(chunking),
            embeddingService,
            chunkRepository,
            loggerFactory.CreateLogger<IngestionPipeline>());

        var collections = new CollectionManager(collectionRepository, _fixture.SchemaInitializer);
        var schemaVersion = await _fixture.SchemaInitializer.GetCurrentSchemaVersionAsync();

        return new MinervaIngestEngine(
            ingestionPipeline,
            collections,
            chunkRepository,
            EmbeddingModel,
            mockEmbeddings,
            chunking,
            schemaVersion,
            loggerFactory.CreateLogger<MinervaIngestEngine>());
    }

    private static ChunkingOptions Chunking(int targetChunkSize) => new()
    {
        TargetChunkSize = targetChunkSize,
        ChunkOverlap = 100,
        ChunkerType = ChunkerType.Custom,
    };

    private static async IAsyncEnumerable<Document> OneDoc()
    {
        yield return new Document(
            SourceId: "doc-a",
            Title: "PostgreSQL Guide",
            Text: "PostgreSQL is a powerful open-source relational database. " +
                  "It supports ACID transactions, JSON, and full-text search.");
        await Task.Yield();
    }
}
