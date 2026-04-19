using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minerva.Collections;
using Minerva.Configuration;
using Minerva.Ingestion;
using Minerva.Providers;
using Minerva.Search;
using Minerva.Storage;
using Npgsql;

namespace Minerva.DI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMinerva(
        this IServiceCollection services,
        Action<MinervaOptions> configure)
    {
        services.Configure(configure);

        services.TryAddSingleton<ProviderFactory>();

        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MinervaOptions>>().Value;
            var builder = new NpgsqlDataSourceBuilder(options.ConnectionString);
            builder.UseVector();
            return builder.Build();
        });

        services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var factory = sp.GetRequiredService<ProviderFactory>();
            var options = sp.GetRequiredService<IOptions<MinervaOptions>>().Value;
            return factory.CreateEmbeddingProvider(options.Embedding);
        });

        // Chat client is optional — only registered when an LLM is configured.
        services.TryAddSingleton<IChatClient>(sp =>
        {
            var factory = sp.GetRequiredService<ProviderFactory>();
            var options = sp.GetRequiredService<IOptions<MinervaOptions>>().Value;
            return factory.CreateLlmProvider(options.Llm)
                ?? throw new InvalidOperationException(
                    "IChatClient requested but MinervaOptions.Llm is not configured.");
        });

        services.TryAddSingleton<SchemaInitializer>();
        services.TryAddSingleton<ICollectionProvisioner>(
            sp => sp.GetRequiredService<SchemaInitializer>());
        services.TryAddSingleton<ICollectionRepository, PostgresCollectionRepository>();
        services.TryAddSingleton<IChunkRepository, PostgresChunkRepository>();

        services.TryAddSingleton<IDocumentChunker>(sp =>
            new DocumentChunker(sp.GetRequiredService<IOptions<MinervaOptions>>().Value.Chunking));

        services.TryAddSingleton<IEmbeddingService>(sp => new EmbeddingService(
            sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            sp.GetRequiredService<IOptions<MinervaOptions>>().Value.Embedding.BatchSize,
            sp.GetRequiredService<ILogger<EmbeddingService>>()));

        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MinervaOptions>>().Value;
            var chatClient = options.Llm is not null ? sp.GetRequiredService<IChatClient>() : null;

            IDocumentSummarizer? summarizer = options.Chunking.EnableSummarization && chatClient is not null
                ? new DocumentSummarizer(chatClient)
                : null;
            IChunkContextualizer? contextualizer = options.Chunking.EnableContextualization && chatClient is not null
                ? new ChunkContextualizer(chatClient)
                : null;

            return new IngestionPipeline(
                sp.GetRequiredService<IDocumentChunker>(),
                sp.GetRequiredService<IEmbeddingService>(),
                summarizer,
                contextualizer,
                sp.GetRequiredService<IChunkRepository>(),
                sp.GetRequiredService<ILogger<IngestionPipeline>>());
        });

        services.TryAddSingleton<VectorSearch>();
        services.TryAddSingleton<FullTextSearch>();
        services.TryAddSingleton<ContextExpander>();
        services.TryAddSingleton<SearchPipeline>();

        services.TryAddSingleton<ICollectionService, CollectionManager>();
        services.TryAddSingleton<IMinervaEngine, MinervaEngine>();

        services.AddHostedService<MinervaStartupService>();

        return services;
    }
}
