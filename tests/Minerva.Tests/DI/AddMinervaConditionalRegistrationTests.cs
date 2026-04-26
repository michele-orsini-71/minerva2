using Microsoft.Extensions.DependencyInjection;
using Minerva.Collections;
using Minerva.DI;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Providers;
using Minerva.Storage;
using Npgsql;

namespace Minerva.Tests.DI;

[Trait("Category", "DI")]
public class AddMinervaConditionalRegistrationTests
{
    [Fact]
    public void NoConfig_BuildsContainerAndResolvesNothingFeatureScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMinerva(_ => { });

        using var sp = services.BuildServiceProvider();

        Assert.Null(sp.GetService<NpgsqlDataSource>());
        Assert.Null(sp.GetService<IEmbeddingClient>());
        Assert.Null(sp.GetService<IEmbeddingDimensionProvider>());
        Assert.Null(sp.GetService<ILlmClient>());
        Assert.Null(sp.GetService<IMinervaEngine>());
        Assert.Null(sp.GetService<ICollectionService>());
    }

    [Fact]
    public void StorageOnly_RegistersStorageNotEmbedderNotLlm()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMinerva(opt => opt.ConnectionString = "Host=localhost;Port=1");

        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<NpgsqlDataSource>());
        Assert.NotNull(sp.GetService<ICollectionService>());
        Assert.NotNull(sp.GetService<SchemaInitializer>());
        Assert.Null(sp.GetService<IEmbeddingClient>());
        Assert.Null(sp.GetService<ILlmClient>());
        Assert.Null(sp.GetService<IMinervaEngine>());
    }

    [Fact]
    public void EmbeddingOnly_RegistersEmbedderNotStorageNotLlm()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMinerva(opt => opt.Embedding = NewEmbeddingOptions());

        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IEmbeddingClient>());
        Assert.NotNull(sp.GetService<IEmbeddingDimensionProvider>());
        Assert.NotNull(sp.GetService<IEmbeddingService>());
        Assert.Null(sp.GetService<NpgsqlDataSource>());
        Assert.Null(sp.GetService<ILlmClient>());
        Assert.Null(sp.GetService<IMinervaEngine>());
    }

    [Fact]
    public void LlmOnly_RegistersLlmNotStorageNotEmbedder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMinerva(opt => opt.Llm = NewLlmOptions());

        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<ILlmClient>());
        Assert.Null(sp.GetService<NpgsqlDataSource>());
        Assert.Null(sp.GetService<IEmbeddingClient>());
        Assert.Null(sp.GetService<IMinervaEngine>());
    }

    [Fact]
    public void StorageAndEmbedding_RegistersEngineAndIngestionPipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMinerva(opt =>
        {
            opt.ConnectionString = "Host=localhost;Port=1";
            opt.Embedding = NewEmbeddingOptions();
        });

        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IMinervaEngine>());
        Assert.NotNull(sp.GetService<IngestionPipeline>());
        Assert.Null(sp.GetService<ILlmClient>());
    }

    [Fact]
    public void FullConfig_RegistersAllFeatures()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMinerva(opt =>
        {
            opt.ConnectionString = "Host=localhost;Port=1";
            opt.Embedding = NewEmbeddingOptions();
            opt.Llm = NewLlmOptions();
        });

        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<NpgsqlDataSource>());
        Assert.NotNull(sp.GetService<IEmbeddingClient>());
        Assert.NotNull(sp.GetService<IEmbeddingDimensionProvider>());
        Assert.NotNull(sp.GetService<ILlmClient>());
        Assert.NotNull(sp.GetService<IMinervaEngine>());
        Assert.NotNull(sp.GetService<IngestionPipeline>());
    }

    private static ProviderOptions NewEmbeddingOptions() => new()
    {
        BaseUrl = "http://localhost",
        Model = "test-embedding",
    };

    private static ProviderOptions NewLlmOptions() => new()
    {
        BaseUrl = "http://localhost",
        Model = "test-llm",
    };
}
