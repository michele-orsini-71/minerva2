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
        // Phase 1: validate option shape (cheap, synchronous).
        var optionFailures = ValidateOptions(options);
        if (optionFailures.Count > 0)
            throw new MinervaStartupException(optionFailures);

        // Phase 2: construct services (no I/O).
        ProviderFactory providerFactory;
        try
        {
            providerFactory = new ProviderFactory(options.Embedding, options.Llm);
        }
        catch (ConfigurationException ex)
        {
            throw new MinervaStartupException(
                [new PreflightFailure("Options.Credentials", ex.Message, ex)]);
        }

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(options.ConnectionString);
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        var schemaInitializer = new SchemaInitializer(
            dataSource,
            loggerFactory.CreateLogger<SchemaInitializer>());

        ICollectionRepository collectionRepository = new PostgresCollectionRepository(dataSource);
        ICollectionProvisioner provisioner = schemaInitializer;

        var chunkRepository = new PostgresChunkRepository(dataSource);
        IChunkWriter chunkWriter = chunkRepository;
        IChunkQuery chunkQuery = chunkRepository;

        var embeddingProvider = (OpenAICompatibleEmbeddingProvider)
            providerFactory.CreateEmbeddingProvider();
        IEmbeddingClient embeddingClient = embeddingProvider;
        IEmbeddingDimensionProvider dimensionProvider = embeddingProvider;
        IEmbeddingService embeddingService = new EmbeddingService(
            embeddingClient,
            options.Embedding.BatchSize,
            loggerFactory.CreateLogger<EmbeddingService>());

        ILlmClient? llmClient = null;
        ILlmAvailabilityProbe? llmProbe = null;
        if (providerFactory.HasLlm)
        {
            var llmProvider = (OpenAICompatibleLlmProvider)providerFactory.CreateLlmProvider();
            llmClient = llmProvider;
            llmProbe = llmProvider;
        }

        IDocumentSummarizer? summarizer =
            options.Chunking.EnableSummarization && llmClient is not null
                ? new DocumentSummarizer(llmClient)
                : null;
        IChunkContextualizer? contextualizer =
            options.Chunking.EnableContextualization && llmClient is not null
                ? new ChunkContextualizer(llmClient)
                : null;

        IDocumentChunker chunker = new DocumentChunker(options.Chunking);

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

        ICollectionService collections = new CollectionManager(collectionRepository, provisioner);

        // Phase 3: preflight — environmental checks, aggregate failures.
        // TODO: each service exposes PreflightAsync(ct) returning PreflightFailure?
        // (null on success). Lift the old Readiness check bodies into the relevant
        // services as preflight methods and call them here.
        var preflightFailures = new List<PreflightFailure>();
        // preflightFailures.AddIfNotNull(await PreflightDataSourceAsync(dataSource, ct));     // connectivity, pg_vector
        // preflightFailures.AddIfNotNull(await embeddingProvider.PreflightAsync(ct));         // /embeddings reachable
        // if (llmProbe is not null)
        //     preflightFailures.AddIfNotNull(await llmProbe.PreflightAsync(ct));              // /chat reachable
        if (preflightFailures.Count > 0)
            throw new MinervaStartupException(preflightFailures);

        // Phase 4: start — schema init / migrations.
        await schemaInitializer.InitializeAsync(ct);

        return new MinervaEngine(
            ingestionPipeline,
            searchPipeline,
            collections,
            loggerFactory.CreateLogger<MinervaEngine>());
    }

    private static List<PreflightFailure> ValidateOptions(MinervaOptions options)
    {
        var failures = new List<PreflightFailure>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            failures.Add(new PreflightFailure("Options", "ConnectionString is required."));

        ValidateProvider("Options.Embedding", options.Embedding, failures);

        if (options.Llm is not null)
            ValidateProvider("Options.Llm", options.Llm, failures);

        ValidateChunking(options.Chunking, failures);

        return failures;
    }

    private static void ValidateProvider(string stage, ProviderOptions p, List<PreflightFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(p.BaseUrl))
            failures.Add(new PreflightFailure(stage, "BaseUrl is required."));
        else if (!Uri.TryCreate(p.BaseUrl, UriKind.Absolute, out _))
            failures.Add(new PreflightFailure(stage, $"BaseUrl is not a valid absolute URI: '{p.BaseUrl}'."));

        if (string.IsNullOrWhiteSpace(p.Model))
            failures.Add(new PreflightFailure(stage, "Model is required."));

        if (p.Concurrency <= 0)
            failures.Add(new PreflightFailure(stage, $"Concurrency must be > 0 (got {p.Concurrency})."));

        if (p.BatchSize <= 0)
            failures.Add(new PreflightFailure(stage, $"BatchSize must be > 0 (got {p.BatchSize})."));

        if (p.RequestsPerMinute is int rpm && rpm <= 0)
            failures.Add(new PreflightFailure(stage, $"RequestsPerMinute must be > 0 when set (got {rpm})."));
    }

    private static void ValidateChunking(ChunkingOptions c, List<PreflightFailure> failures)
    {
        const string stage = "Options.Chunking";

        if (c.TargetChunkSize <= 0)
            failures.Add(new PreflightFailure(stage, $"TargetChunkSize must be > 0 (got {c.TargetChunkSize})."));

        if (c.ChunkOverlap < 0)
            failures.Add(new PreflightFailure(stage, $"ChunkOverlap must be >= 0 (got {c.ChunkOverlap})."));
        else if (c.TargetChunkSize > 0 && c.ChunkOverlap >= c.TargetChunkSize)
            failures.Add(new PreflightFailure(stage, $"ChunkOverlap must be < TargetChunkSize (got {c.ChunkOverlap} >= {c.TargetChunkSize})."));

        if (c.LargeDocumentThreshold <= 0)
            failures.Add(new PreflightFailure(stage, $"LargeDocumentThreshold must be > 0 (got {c.LargeDocumentThreshold})."));
    }
}
