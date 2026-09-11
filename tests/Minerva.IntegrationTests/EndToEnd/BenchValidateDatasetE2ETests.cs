using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Minerva.Collections;
using Minerva.Ingestion;
using Minerva.IntegrationTests.Storage;
using Minerva.IntegrationTests.TestSupport;
using Minerva.Models;
using Minerva.Search;
using Minerva.Search.Bench.Validation;
using Minerva.Storage;

namespace Minerva.IntegrationTests.EndToEnd;

[Collection("Storage")]
[Trait("Category", "E2E")]
public class BenchValidateDatasetE2ETests : IAsyncLifetime
{
    private const int EmbeddingDimension = 16;
    private const string CollectionName = "bench-e2e-collection";
    private const string EmbeddingModel = "mock-embedding";

    private readonly StorageTestFixture _fixture;
    private readonly ISearchEngine _search;
    private readonly IIngestEngine _ingest;

    public BenchValidateDatasetE2ETests(StorageTestFixture fixture)
    {
        _fixture = fixture;

        var chunking = new ChunkingOptions
        {
            TargetChunkSize = 600,
            ChunkOverlap = 100,
            ChunkerType = ChunkerType.Custom,
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
            chunkRepository,
            loggerFactory.CreateLogger<IngestionPipeline>());

        var searchPipeline = new SearchPipeline(
            embeddingService,
            new VectorSearch(chunkRepository),
            new FullTextSearch(chunkRepository),
            new ContextExpander(chunkRepository),
            new NoopReranker(),
            loggerFactory.CreateLogger<SearchPipeline>());

        var collections = new CollectionManager(
            collectionRepository, fixture.SchemaInitializer);

        var searchDefaults = new SearchOptions
        {
            TopK = 5,
            HybridAlpha = 0.5,
            CandidatePoolSize = 25,
            ExpandContext = false,
            EnableReranker = true,
        };

        _search = new MinervaSearchEngine(searchPipeline, collections, chunkRepository, searchDefaults);
        _ingest = new MinervaIngestEngine(
            ingestionPipeline,
            collections,
            chunkRepository,
            EmbeddingModel,
            mockEmbeddings,
            chunking,
            "003_collection_provenance",
            loggerFactory.CreateLogger<MinervaIngestEngine>());
    }

    public Task InitializeAsync() => _fixture.CleanupAsync(honorDisableFlag: false);
    public Task DisposeAsync() => _fixture.CleanupAsync();

    [Fact]
    public async Task ValidateDataset_OnePassOneMiss_ReportsMissAndReturnsFailureCode()
    {
        await SeedAsync("doc-a", "doc-b");

        var jsonlPath = WriteTempJsonl(
            """{"id":"q001","query":"about doc a","gold_sources":["doc-a"]}""",
            """{"id":"q002","query":"about ghost","gold_sources":["doc-z-missing"]}""");

        try
        {
            using var sw = new StringWriter();
            var exitCode = await DatasetValidationRunner.RunAsync(
                _search, jsonlPath, CollectionName, sw);

            var output = sw.ToString();

            Assert.Equal(2, exitCode);
            Assert.Contains("q002", output);
            Assert.Contains("doc-z-missing", output);
            Assert.Contains($"gold_source not found in collection '{CollectionName}'", output);
            Assert.DoesNotContain("doc-a", output);  // passing entry must not appear in problem groups
            Assert.Contains("validation failed", output);
        }
        finally
        {
            File.Delete(jsonlPath);
        }
    }

    [Fact]
    public async Task ValidateDataset_AllPass_ReportsSuccessAndReturnsZero()
    {
        await SeedAsync("doc-a", "doc-b");

        var jsonlPath = WriteTempJsonl(
            """{"id":"q001","query":"about doc a","gold_sources":["doc-a"]}""",
            """{"id":"q002","query":"about doc b","gold_sources":["doc-b","doc-a"]}""");

        try
        {
            using var sw = new StringWriter();
            var exitCode = await DatasetValidationRunner.RunAsync(
                _search, jsonlPath, CollectionName, sw);

            var output = sw.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains("validation passed", output);
            Assert.Contains("2 entries", output);
            Assert.Contains("3 gold_sources resolved", output);
        }
        finally
        {
            File.Delete(jsonlPath);
        }
    }

    private async Task SeedAsync(params string[] sourceIds)
    {
        var docs = sourceIds.Select(id => new Document(
            SourceId: id,
            Title: id,
            Text: $"Content about {id}. Some additional sentence so the chunker is happy.")).ToArray();
        await _ingest.IngestAsync(CollectionName, TestProvenance.Client(), AsAsync(docs));
    }

    private static string WriteTempJsonl(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bench-test-{Guid.NewGuid():N}.jsonl");
        File.WriteAllLines(path, lines);
        return path;
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
