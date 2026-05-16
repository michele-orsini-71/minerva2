using Microsoft.Extensions.Logging;
using Minerva.Collections;
using Minerva.Exceptions;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Providers;
using Minerva.Search;
using Minerva.Storage;
using Npgsql;

namespace Minerva;

internal sealed record MinervaCoreServices(
    NpgsqlDataSource DataSource,
    DatabasePreflight DatabasePreflight,
    SchemaInitializer SchemaInitializer,
    IChunkWriter ChunkWriter,
    IChunkQuery ChunkQuery,
    OpenAICompatibleEmbeddingProvider EmbeddingProvider,
    IEmbeddingService EmbeddingService,
    ICollectionService Collections);

internal static class MinervaCore
{
    public static MinervaCoreServices Build(
        string connectionString,
        EmbeddingProviderOptions embedding,
        ILoggerFactory loggerFactory)
    {
        NpgsqlDataSource dataSource;
        try
        {
            var b = new NpgsqlDataSourceBuilder(connectionString);
            b.UseVector();
            dataSource = b.Build();
        }
        catch (ArgumentException ex)
        {
            throw new MinervaStartupException(
                [new PreflightFailure("DataSource", ex.Message, ex)]);
        }

        var databasePreflight = new DatabasePreflight(dataSource);
        var schemaInitializer = new SchemaInitializer(
            dataSource, loggerFactory.CreateLogger<SchemaInitializer>());

        var chunkRepository = new PostgresChunkRepository(dataSource);
        IChunkWriter chunkWriter = chunkRepository;
        IChunkQuery chunkQuery = chunkRepository;

        OpenAICompatibleEmbeddingProvider embeddingProvider;
        try
        {
            // ProviderFactory's LLM is search/ingest specific; for embedding only,
            // pass null and let the factory skip LLM wiring.
            var providerFactory = new ProviderFactory(embedding, llm: null);
            embeddingProvider = (OpenAICompatibleEmbeddingProvider)
                providerFactory.CreateEmbeddingProvider();
        }
        catch (ConfigurationException ex)
        {
            throw new MinervaStartupException(
                [new PreflightFailure("Embedding.Credentials", ex.Message, ex)]);
        }

        IEmbeddingService embeddingService = new EmbeddingService(
            embeddingProvider,
            embedding.BatchSize,
            loggerFactory.CreateLogger<EmbeddingService>());

        ICollectionRepository collectionRepository = new PostgresCollectionRepository(dataSource);
        ICollectionService collections = new CollectionManager(
            collectionRepository, schemaInitializer, embedding.Model, embeddingProvider);

        return new MinervaCoreServices(
            DataSource: dataSource,
            DatabasePreflight: databasePreflight,
            SchemaInitializer: schemaInitializer,
            ChunkWriter: chunkWriter,
            ChunkQuery: chunkQuery,
            EmbeddingProvider: embeddingProvider,
            EmbeddingService: embeddingService,
            Collections: collections);
    }
}
