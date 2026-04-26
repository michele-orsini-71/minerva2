using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minerva.Collections;
using Minerva.Configuration;
using Minerva.Ingestion;
using Minerva.Providers;
using Minerva.Readiness;
using Minerva.Readiness.Checks;
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
        var options = new MinervaOptions();
        configure(options);
        services.Configure(configure);

        services.TryAddSingleton<ProviderFactory>();

        services.TryAddSingleton<IDocumentChunker>(sp =>
            new DocumentChunker(sp.GetRequiredService<IOptions<MinervaOptions>>().Value.Chunking));

        if (options.ConnectionString is not null)
        {
            services.TryAddSingleton(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<MinervaOptions>>().Value;
                var builder = new NpgsqlDataSourceBuilder(opts.ConnectionString);
                builder.UseVector();
                return builder.Build();
            });

            services.TryAddSingleton<SchemaInitializer>();
            services.TryAddSingleton<ICollectionProvisioner>(
                sp => sp.GetRequiredService<SchemaInitializer>());
            services.TryAddSingleton<ICollectionRepository, PostgresCollectionRepository>();

            services.TryAddSingleton<PostgresChunkRepository>();
            services.TryAddSingleton<IChunkWriter>(sp => sp.GetRequiredService<PostgresChunkRepository>());
            services.TryAddSingleton<IChunkQuery>(sp => sp.GetRequiredService<PostgresChunkRepository>());

            services.TryAddSingleton<VectorSearch>();
            services.TryAddSingleton<FullTextSearch>();
            services.TryAddSingleton<ContextExpander>();
            services.TryAddSingleton<SearchPipeline>();

            services.TryAddSingleton<ICollectionService, CollectionManager>();

            services.AddHostedService<MinervaStartupService>();
        }

        if (options.Embedding is not null)
        {
            services.TryAddSingleton<OpenAICompatibleEmbeddingProvider>(sp =>
            {
                var factory = sp.GetRequiredService<ProviderFactory>();
                var opts = sp.GetRequiredService<IOptions<MinervaOptions>>().Value;
                return (OpenAICompatibleEmbeddingProvider)factory.CreateEmbeddingProvider(opts.Embedding!);
            });
            services.TryAddSingleton<IEmbeddingClient>(sp => sp.GetRequiredService<OpenAICompatibleEmbeddingProvider>());
            services.TryAddSingleton<IEmbeddingDimensionProvider>(sp => sp.GetRequiredService<OpenAICompatibleEmbeddingProvider>());

            services.TryAddSingleton<IEmbeddingService>(sp => new EmbeddingService(
                sp.GetRequiredService<IEmbeddingClient>(),
                sp.GetRequiredService<IOptions<MinervaOptions>>().Value.Embedding!.BatchSize,
                sp.GetRequiredService<ILogger<EmbeddingService>>()));
        }

        if (options.Llm is not null)
        {
            services.TryAddSingleton<OpenAICompatibleLlmProvider>(sp =>
            {
                var factory = sp.GetRequiredService<ProviderFactory>();
                var opts = sp.GetRequiredService<IOptions<MinervaOptions>>().Value;
                return (OpenAICompatibleLlmProvider)(factory.CreateLlmProvider(opts.Llm)
                    ?? throw new InvalidOperationException(
                        "ILlmClient requested but MinervaOptions.Llm is not configured."));
            });
            services.TryAddSingleton<ILlmClient>(sp => sp.GetRequiredService<OpenAICompatibleLlmProvider>());
            services.TryAddSingleton<ILlmAvailabilityProbe>(sp => sp.GetRequiredService<OpenAICompatibleLlmProvider>());
        }

        if (options.ConnectionString is not null && options.Embedding is not null)
        {
            services.TryAddSingleton(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<MinervaOptions>>().Value;
                var llm = opts.Llm is not null ? sp.GetRequiredService<ILlmClient>() : null;

                IDocumentSummarizer? summarizer = opts.Chunking.EnableSummarization && llm is not null
                    ? new DocumentSummarizer(llm)
                    : null;
                IChunkContextualizer? contextualizer = opts.Chunking.EnableContextualization && llm is not null
                    ? new ChunkContextualizer(llm)
                    : null;

                return new IngestionPipeline(
                    sp.GetRequiredService<IDocumentChunker>(),
                    sp.GetRequiredService<IEmbeddingService>(),
                    summarizer,
                    contextualizer,
                    sp.GetRequiredService<IChunkWriter>(),
                    sp.GetRequiredService<ILogger<IngestionPipeline>>());
            });

            services.TryAddSingleton<IMinervaEngine, MinervaEngine>();
        }

        services.AddMinervaReadinessCore();
        services.AddMinervaReadinessCheck<ConnectionStringParseCheck>();
        services.AddMinervaReadinessCheck<PostgresConnectivityCheck>();
        services.AddMinervaReadinessCheck<PgVectorExtensionCheck>();
        services.AddMinervaReadinessCheck<EmbeddingCallCheck>();
        services.AddMinervaReadinessCheck<LlmCallCheck>();

        return services;
    }
}
