using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Minerva.Collections;
using Minerva.Configuration;
using Minerva.Ingestion;
using Minerva.IntegrationTests.Storage;
using Minerva.IntegrationTests.TestSupport;
using Minerva.Models;
using Minerva.Search;
using Minerva.Search.Bench.Sweep;
using Minerva.Storage;

namespace Minerva.IntegrationTests.EndToEnd;

[Collection("Storage")]
[Trait("Category", "E2E")]
public class BenchRunE2ETests : IAsyncLifetime
{
    private const int EmbeddingDimension = 16;
    private const string CollectionName = "bench-run-e2e-collection";
    private const string EmbeddingModel = "mock-embedding";

    private readonly StorageTestFixture _fixture;
    private readonly ISearchEngine _search;
    private readonly IIngestEngine _ingest;
    private readonly MinervaSearchOptions _minervaOptions;

    public BenchRunE2ETests(StorageTestFixture fixture)
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

        var chunkRepository = new PostgresSourceRepository(fixture.DataSource);
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
            RerankDepth = null,
            CascadeDepth = null,
        };

        _minervaOptions = new MinervaSearchOptions
        {
            ConnectionString = fixture.DataSource.ConnectionString,
            Embedding = new EmbeddingProviderOptions
            {
                BaseUrl = "http://localhost:11434/v1",
                Model = EmbeddingModel,
                Concurrency = 1,
                BatchSize = 4,
            },
            Reranker = null,
            CascadeReranker = null,
            TopK = searchDefaults.TopK,
            HybridAlpha = searchDefaults.HybridAlpha,
            CandidatePoolSize = searchDefaults.CandidatePoolSize,
            ExpandContext = searchDefaults.ExpandContext,
            EnableReranker = searchDefaults.EnableReranker,
            RerankDepth = searchDefaults.RerankDepth,
            CascadeDepth = searchDefaults.CascadeDepth,
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
    public async Task Run_ThreeQueriesOneCell_WritesAllThreeOutputFiles()
    {
        await SeedAsync("doc-a", "doc-b", "doc-c");

        var workDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllLines(Path.Combine(workDir, "dataset.jsonl"),
            [
                """{"id":"q1","query":"about doc a","gold_sources":["doc-a"]}""",
                """{"id":"q2","query":"about doc b","gold_sources":["doc-b"]}""",
                """{"id":"q3","query":"about doc c","gold_sources":["doc-c"]}""",
            ]);

            var sweepPath = Path.Combine(workDir, "sweep.toml");
            File.WriteAllText(sweepPath,
                $"""
                dataset = "dataset.jsonl"
                collection = "{CollectionName}"
                top_k = 5
                candidate_pool_size = 25

                [matrix]
                enable_reranker = [false]
                """);

            var outDir = Path.Combine(workDir, "out");
            Directory.CreateDirectory(outDir);

            using var sw = new StringWriter();
            var exitCode = await SweepDatasetRunner.RunAsync(_search, sweepPath, outDir, _minervaOptions, sw);

            Assert.Equal(0, exitCode);

            var leaf = Assert.Single(Directory.GetDirectories(outDir));
            Assert.True(File.Exists(Path.Combine(leaf, "run.json")));
            Assert.True(File.Exists(Path.Combine(leaf, "metrics.csv")));
            Assert.True(File.Exists(Path.Combine(leaf, "details.jsonl")));

            // metrics.csv: header + one row per (query x cell) = 3 rows
            var csvLines = File.ReadAllLines(Path.Combine(leaf, "metrics.csv"));
            Assert.Equal(4, csvLines.Length);
            Assert.StartsWith("query_id,enable_reranker,", csvLines[0]);

            // details.jsonl: one line per (query x cell)
            Assert.Equal(3, File.ReadAllLines(Path.Combine(leaf, "details.jsonl")).Length);

            // run.json: collection provenance
            var run = JsonDocument.Parse(File.ReadAllText(Path.Combine(leaf, "run.json"))).RootElement;
            Assert.Equal(CollectionName, run.GetProperty("collection").GetString());
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
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

    private static async IAsyncEnumerable<Document> AsAsync(params Document[] docs)
    {
        foreach (var d in docs)
        {
            yield return d;
            await Task.Yield();
        }
    }
}
