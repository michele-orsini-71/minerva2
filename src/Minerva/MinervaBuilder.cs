using Microsoft.Extensions.Logging;
using Minerva.Collections;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Providers;
using Minerva.Search;
using Minerva.Storage;
using Npgsql;

namespace Minerva;

public static class MinervaBuilder
{
    public static async Task<IMinervaEngine> CreateAsync(
        MinervaOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken ct = default)
    {
        // Phase 2: construct services (no I/O). Options arrive pre-validated from the binder.
        ProviderFactory providerFactory;
        try
        {
            providerFactory = new ProviderFactory(options.Embedding, options.Chunking.Llm);
        }
        catch (ConfigurationException ex)
        {
            throw new MinervaStartupException(
                [new PreflightFailure("Options.Credentials", ex.Message, ex)]);
        }

        NpgsqlDataSource? dataSource = null;
        try
        {
            var b = new NpgsqlDataSourceBuilder(options.ConnectionString);
            b.UseVector();
            dataSource = b.Build();
        }
        catch (ArgumentException ex)   // Npgsql's shape failures
        {
            throw new MinervaStartupException(
                [new PreflightFailure("DataSource", ex.Message, ex)]);
        }

        var databasePreflight = new DatabasePreflight(dataSource);

        var schemaInitializer = new SchemaInitializer(
            dataSource,
            loggerFactory.CreateLogger<SchemaInitializer>());

        ICollectionRepository collectionRepository = new PostgresCollectionRepository(dataSource);
        ICollectionProvisioner provisioner = schemaInitializer;

        var chunkRepository = new PostgresChunkRepository(dataSource);
        IChunkWriter chunkWriter = chunkRepository;
        IChunkQuery chunkQuery = chunkRepository;

        OpenAICompatibleEmbeddingProvider embeddingProvider;
        try {
            embeddingProvider = (OpenAICompatibleEmbeddingProvider)
                providerFactory.CreateEmbeddingProvider();
        }
        catch (ConfigurationException ex)
        {
            throw new MinervaStartupException(
                [new PreflightFailure("Embedding.Credentials", ex.Message, ex)]);
        }

        IEmbeddingClient embeddingClient = embeddingProvider;
        IEmbeddingService embeddingService = new EmbeddingService(
            embeddingClient,
            options.Embedding.BatchSize,
            loggerFactory.CreateLogger<EmbeddingService>());

        OpenAICompatibleLlmProvider? llmProvider = null;
        ILlmClient? llmClient = null;
        if (providerFactory.HasLlm)
        {
            try
            {
                llmProvider = (OpenAICompatibleLlmProvider)providerFactory.CreateLlmProvider(
                    loggerFactory.CreateLogger<OpenAICompatibleLlmProvider>());
                llmClient = llmProvider;
            }
            catch (ConfigurationException ex)
            {
                throw new MinervaStartupException(
                    [new PreflightFailure("Llm.Credentials", ex.Message, ex)]);
            }
        }

        IDocumentSummarizer? summarizer = llmClient is not null
            ? new DocumentSummarizer(llmClient)
            : null;
        IChunkContextualizer? contextualizer = llmClient is not null
            ? new ChunkContextualizer(llmClient)
            : null;

        IDocumentChunker chunker = options.Chunking.ChunkerType switch
        {
            ChunkerType.SemanticKernel => new SemanticKernelChunker(options.Chunking),
            _ => new DocumentChunker(
                options.Chunking, loggerFactory.CreateLogger<DocumentChunker>()),
        };

        var ingestionPipeline = new IngestionPipeline(
            chunker,
            embeddingService,
            summarizer,
            contextualizer,
            chunkWriter,
            loggerFactory.CreateLogger<IngestionPipeline>());

        var vectorSearch = new VectorSearch(chunkQuery);
        var fullTextSearch = new FullTextSearch(chunkQuery);
        var contextExpander = new ContextExpander(chunkQuery);
        var searchPipeline = new SearchPipeline(
            embeddingService,
            vectorSearch,
            fullTextSearch,
            contextExpander,
            loggerFactory.CreateLogger<SearchPipeline>());

        ICollectionService collections = new CollectionManager(
            collectionRepository, provisioner, options.Embedding.Model, embeddingProvider);

        // Phase 3: preflight — environmental checks, aggregate failures.
        var preflightFailures = await RunPreflightAsync(
            databasePreflight,
            embeddingProvider,
            llmProvider,
            options.Chunking.ContextBudget.MaxContextTokens,
            loggerFactory.CreateLogger<OpenAICompatibleLlmProvider>(),
            ct);
        if (preflightFailures.Count > 0)
            throw new MinervaStartupException(preflightFailures);

        // Phase 4: start — schema init / migrations.
        await schemaInitializer.InitializeAsync(ct);

        return new MinervaEngine(
            ingestionPipeline,
            searchPipeline,
            collections,
            chunkWriter,
            options.Embedding.Model,
            embeddingProvider,
            loggerFactory.CreateLogger<MinervaEngine>());
    }

    private static async Task<List<PreflightFailure>> RunPreflightAsync(
        DatabasePreflight databasePreflight,
        OpenAICompatibleEmbeddingProvider embeddingProvider,
        OpenAICompatibleLlmProvider? llmProvider,
        int configuredMaxContextTokens,
        ILogger<OpenAICompatibleLlmProvider> llmLogger,
        CancellationToken ct)
    {
        var failures = new List<PreflightFailure>();

        var storageFailure = await databasePreflight.PreflightAsync(ct);
        if (storageFailure is not null)
            failures.Add(storageFailure);

        var embeddingFailure = await embeddingProvider.PreflightAsync(ct);
        if (embeddingFailure is not null)
            failures.Add(embeddingFailure);

        if (llmProvider is not null)
        {
            var llmFailure = await llmProvider.PreflightAsync(configuredMaxContextTokens, llmLogger, ct);
            if (llmFailure is not null)
                failures.Add(llmFailure);
        }

        return failures;
    }
}
