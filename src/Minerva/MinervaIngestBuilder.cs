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
        var options = MinervaIngestOptionsBinder.Bind(configurationSection);

        var core = MinervaCore.Build(options.ConnectionString, options.Embedding, loggerFactory);

        IDocumentChunker chunker = options.Chunking.ChunkerType switch
        {
            ChunkerType.SemanticKernel => new SemanticKernelChunker(options.Chunking),
            _ => new DocumentChunker(
                options.Chunking, loggerFactory.CreateLogger<DocumentChunker>()),
        };

        var ingestionPipeline = new IngestionPipeline(
            chunker,
            core.EmbeddingService,
            core.ChunkWriter,
            loggerFactory.CreateLogger<IngestionPipeline>());

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
