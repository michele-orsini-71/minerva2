using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Providers;

namespace Minerva;

public static class MinervaIngestBuilder
{
    public static async Task<IIngestEngine> CreateAsync(
        IConfiguration configurationSection,
        ILoggerFactory loggerFactory,
        CancellationToken ct = default)
    {
        // Phase 1: bind + validate options.
        var options = MinervaIngestOptionsBinder.Bind(configurationSection);

        // Phase 2: construct (no I/O). Options arrive pre-validated.
        var core = MinervaCore.Build(options.ConnectionString, options.Embedding, loggerFactory);

        OpenAICompatibleLlmProvider? llmProvider = null;
        ILlmClient? llmClient = null;
        if (options.Chunking.Contextualization.Llm is not null)
        {
            try
            {
                var providerFactory = new ProviderFactory(options.Embedding, options.Chunking.Contextualization.Llm);
                llmProvider = (OpenAICompatibleLlmProvider)providerFactory.CreateLlmProvider();
                llmClient = llmProvider;
            }
            catch (ConfigurationException ex)
            {
                throw new MinervaStartupException(
                    [new PreflightFailure("Llm.Credentials", ex.Message, ex)]);
            }
        }

        // Binder validation guarantees Llm (hence llmClient) is present exactly
        // when the level is DocumentBrief or PerChunk.
        var level = options.Chunking.Contextualization.Level;
        IDocumentSummarizer? summarizer = level >= ContextualizationLevel.DocumentBrief
            ? new DocumentSummarizer(llmClient!)
            : null;
        IChunkContextualizer? contextualizer = level == ContextualizationLevel.PerChunk
            ? new ChunkContextualizer(llmClient!)
            : null;

        IDocumentChunker chunker = options.Chunking.ChunkerType switch
        {
            ChunkerType.SemanticKernel => new SemanticKernelChunker(options.Chunking),
            _ => new DocumentChunker(
                options.Chunking, loggerFactory.CreateLogger<DocumentChunker>()),
        };

        var ingestionPipeline = new IngestionPipeline(
            chunker,
            core.EmbeddingService,
            summarizer,
            contextualizer,
            level,
            core.ChunkWriter,
            loggerFactory.CreateLogger<IngestionPipeline>());

        // Phase 3: preflight — DB + embedding + LLM (if configured).
        var failures = new List<PreflightFailure>(await core.PreflightAsync(ct));

        if (llmProvider is not null && await llmProvider.PreflightAsync(ct) is { } llmFailure)
            failures.Add(llmFailure);

        if (failures.Count > 0)
            throw new MinervaStartupException(failures);

        // Phase 4: schema init.
        await core.SchemaInitializer.InitializeAsync(ct);
        var schemaVersion = await core.SchemaInitializer.GetCurrentSchemaVersionAsync(ct);

        return new MinervaIngestEngine(
            ingestionPipeline,
            core.Collections,
            core.ChunkWriter,
            options.Embedding.Model,
            core.EmbeddingProvider,
            options.Chunking,
            schemaVersion,
            loggerFactory.CreateLogger<MinervaIngestEngine>());
    }
}
