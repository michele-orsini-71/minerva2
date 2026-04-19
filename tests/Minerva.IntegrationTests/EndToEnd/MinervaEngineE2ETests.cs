using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Minerva.Configuration;
using Minerva.DI;
using Minerva.Models;
using Minerva.Storage;
using Npgsql;

namespace Minerva.IntegrationTests.EndToEnd;

[Collection("Storage")]
[Trait("Category", "E2E")]
public class MinervaEngineE2ETests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Database=minerva_test;Username=postgres;Password=postgres";
    private const int EmbeddingDimension = 16;
    private const string CollectionName = "e2e-collection";

    private ServiceProvider _provider = null!;
    private IMinervaEngine _engine = null!;
    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("MINERVA_TEST_CONNSTRING")
            ?? DefaultConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();

        // Pre-register mock embedding so AddMinerva's TryAdd doesn't overwrite it.
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            new MockEmbeddingGenerator(EmbeddingDimension));

        services.AddMinerva(options =>
        {
            options.ConnectionString = connectionString;
            options.Embedding = new ProviderOptions
            {
                BaseUrl = "http://localhost:0/",
                Model = "mock-embedding",
                BatchSize = 4,
            };
            options.Chunking = new ChunkingOptions
            {
                TargetChunkSize = 600,
                ChunkOverlap = 100,
                EnableSummarization = false,
                EnableContextualization = false,
            };
        });

        _provider = services.BuildServiceProvider();
        _dataSource = _provider.GetRequiredService<NpgsqlDataSource>();

        await CleanDatabaseAsync();
        await _provider.GetRequiredService<SchemaInitializer>().InitializeAsync();

        _engine = _provider.GetRequiredService<IMinervaEngine>();
    }

    public async Task DisposeAsync()
    {
        await CleanDatabaseAsync();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task IngestSearchRemove_FullCycle()
    {
        // 1. Create the collection.
        await _engine.Collections.CreateAsync(
            CollectionName, "mock-embedding", EmbeddingDimension,
            description: "E2E test collection");

        var created = await _engine.Collections.GetAsync(CollectionName);
        Assert.NotNull(created);
        Assert.Equal(EmbeddingDimension, created.EmbeddingDimension);

        // 2. Ingest two documents.
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

        var resultA = await _engine.IngestAsync(CollectionName, docA);
        var resultB = await _engine.IngestAsync(CollectionName, docB);

        Assert.Equal(1, resultA.Added);
        Assert.Equal(1, resultB.Added);

        // 3. Re-ingesting an unchanged document is a no-op.
        var resultANoop = await _engine.IngestAsync(CollectionName, docA);
        Assert.Equal(1, resultANoop.Unchanged);
        Assert.Equal(0, resultANoop.Added);
        Assert.Equal(0, resultANoop.Updated);

        // 4. Search for a term that appears in docA — it should dominate.
        var dbResults = await _engine.SearchAsync(
            "relational database PostgreSQL",
            [CollectionName],
            new SearchOptions(TopK: 5));

        Assert.NotEmpty(dbResults);
        Assert.Equal("doc-a", dbResults[0].SourceId);

        // 5. Remove docA, search again — it should no longer appear.
        await _engine.RemoveAsync(CollectionName, "doc-a");

        var afterRemove = await _engine.SearchAsync(
            "relational database PostgreSQL",
            [CollectionName],
            new SearchOptions(TopK: 5));

        Assert.DoesNotContain(afterRemove, r => r.SourceId == "doc-a");

        // 6. Delete the collection.
        await _engine.Collections.DeleteAsync(CollectionName);
        Assert.Null(await _engine.Collections.GetAsync(CollectionName));
    }

    [Fact]
    public async Task SearchAsync_UnknownCollection_Throws()
    {
        await Assert.ThrowsAsync<Exceptions.ConfigurationException>(() =>
            _engine.SearchAsync("anything", ["does-not-exist"]));
    }

    [Fact]
    public async Task IngestAsync_UnknownCollection_Throws()
    {
        var doc = new Document("orphan", "t", "body text");
        await Assert.ThrowsAsync<Exceptions.ConfigurationException>(() =>
            _engine.IngestAsync("does-not-exist", doc));
    }

    private async Task CleanDatabaseAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM chunks; DELETE FROM collections;", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
