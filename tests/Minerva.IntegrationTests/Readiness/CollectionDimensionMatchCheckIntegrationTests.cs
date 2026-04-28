using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minerva.Configuration;
using Minerva.Ingestion;
using Minerva.IntegrationTests.Storage;
using Minerva.MarkdownWatcher;
using Minerva.MarkdownWatcher.Readiness;
using Minerva.Models;
using Npgsql;

namespace Minerva.IntegrationTests.Readiness;

[Collection("Storage")]
[Trait("Category", "Readiness")]
public class CollectionDimensionMatchCheckIntegrationTests
{
    private readonly StorageTestFixture _fixture;

    public CollectionDimensionMatchCheckIntegrationTests(StorageTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Branch2_PassesTrivially_WhenCollectionsTableMissing()
    {
        // Build a separate data source whose connections set search_path to a schema
        // that does not contain `collections`. The probe's `SELECT … FROM collections`
        // raises 42P01, which the probe translates to null → check passes trivially.
        var baseConnectionString = Environment.GetEnvironmentVariable("MINERVA_TEST_CONNSTRING")
            ?? throw new InvalidOperationException("MINERVA_TEST_CONNSTRING not set");
        var hiddenConnectionString = baseConnectionString + ";Options=-c search_path=pg_temp";

        await using var hiddenDs = NpgsqlDataSource.Create(hiddenConnectionString);

        var check = BuildCheck(hiddenDs, dimension: 768, collectionName: "any-name");

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed, $"Expected pass, got Code={result.Code}, Message={result.Message}");
        Assert.Equal("MINERVA.CLIENT.DIMENSION_OK", result.Code);
    }

    [Fact]
    public async Task Branch3_PassesTrivially_WhenNoRowMatchesCollectionName()
    {
        var uniqueName = "absent-" + Guid.NewGuid().ToString("N")[..8];
        var check = BuildCheck(_fixture.DataSource, dimension: 768, collectionName: uniqueName);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed, $"Expected pass, got Code={result.Code}, Message={result.Message}");
        Assert.Equal("MINERVA.CLIENT.DIMENSION_OK", result.Code);
    }

    [Fact]
    public async Task Branch4_Fails_WhenStoredDimensionDiffersFromProbed()
    {
        var collectionName = "mismatch-" + Guid.NewGuid().ToString("N")[..8];

        await SeedCollectionAsync(collectionName, embeddingDimension: 1024);
        try
        {
            var check = BuildCheck(_fixture.DataSource, dimension: 768, collectionName);

            var result = await check.RunAsync(CancellationToken.None);

            Assert.False(result.Passed);
            Assert.Equal("MINERVA.CLIENT.DIMENSION_MISMATCH", result.Code);
            Assert.Contains("768", result.Remediation);
            Assert.Contains("1024", result.Remediation);
            Assert.Contains($"DELETE FROM collections WHERE name='{collectionName}'", result.Remediation);
        }
        finally
        {
            await DeleteCollectionAsync(collectionName);
        }
    }

    private CollectionDimensionMatchCheck BuildCheck(
        NpgsqlDataSource dataSource,
        int dimension,
        string collectionName)
    {
        var minervaOptions = Options.Create(new MinervaOptions
        {
            ConnectionString = "Host=ignored;Username=u;Password=p;Database=d",
            Embedding = new ProviderOptions { BaseUrl = "http://embed.example", Model = "any" },
        });
        var watcherOptions = Options.Create(new WatcherOptions
        {
            RootPath = "/tmp",
            CollectionName = collectionName,
        });
        var services = new ServiceCollection();
        services.AddSingleton(dataSource);
        services.AddSingleton<IEmbeddingDimensionProvider>(new ConstantDimProvider(dimension));
        var sp = services.BuildServiceProvider();

        return new CollectionDimensionMatchCheck(
            minervaOptions,
            watcherOptions,
            sp,
            NullLogger<CollectionDimensionMatchCheck>.Instance);
    }

    private async Task SeedCollectionAsync(string name, int embeddingDimension)
    {
        await using var conn = await _fixture.DataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO collections (name, embedding_model, embedding_dimension) VALUES (@name, @model, @dim)",
            conn);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("model", "test-model");
        cmd.Parameters.AddWithValue("dim", embeddingDimension);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task DeleteCollectionAsync(string name)
    {
        await using var conn = await _fixture.DataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM collections WHERE name = @name",
            conn);
        cmd.Parameters.AddWithValue("name", name);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class ConstantDimProvider : IEmbeddingDimensionProvider
    {
        private readonly int _dim;
        public ConstantDimProvider(int dim) => _dim = dim;
        public Task<int> GetDimensionAsync(CancellationToken ct = default) => Task.FromResult(_dim);
    }
}
