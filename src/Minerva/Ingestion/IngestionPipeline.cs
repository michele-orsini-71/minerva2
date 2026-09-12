using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Utilities;

namespace Minerva.Ingestion;

internal class IngestionPipeline
{
    private readonly IDocumentChunker _chunker;
    private readonly IEmbeddingService _embeddingService;
    private readonly ISourceWriter _sourceWriter;
    private readonly ILogger<IngestionPipeline> _logger;

    public IngestionPipeline(
        IDocumentChunker chunker,
        IEmbeddingService embeddingService,
        ISourceWriter sourceWriter,
        ILogger<IngestionPipeline> logger)
    {
        _chunker = chunker;
        _embeddingService = embeddingService;
        _sourceWriter = sourceWriter;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestAsync(
        string collectionName,
        Document document,
        string? storedContentHash,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // 1. Integrate attachments
        var text = AttachmentIntegrator.Integrate(
            document.Text, document.Attachments ?? new Dictionary<string, AttachmentDescription>());

        if (string.IsNullOrWhiteSpace(text))
        {
            if (storedContentHash is not null)
            {
                _logger.LogInformation(
                    "Document {SourceId} is now empty; removing prior chunks", document.SourceId);
                await _sourceWriter.DeleteBySourceIdAsync(collectionName, document.SourceId, ct);
                return new IngestionResult(0, 0, Deleted: 1, 0, sw.Elapsed, 0);
            }

            _logger.LogDebug("Document {SourceId} is empty, skipping", document.SourceId);
            return new IngestionResult(0, 0, 0, 0, sw.Elapsed, 0);
        }

        var contentHash = HashHelper.ComputeContentHash(text);
        if (storedContentHash == contentHash)
        {
            _logger.LogDebug("Document {SourceId} unchanged, skipping ingestion", document.SourceId);
            return new IngestionResult(0, 0, 0, Unchanged: 1, sw.Elapsed, 0);
        }

        bool isUpdate = storedContentHash is not null;

        List<Chunk> allChunks = _chunker.Chunk(collectionName, document.SourceId, text).ToList();
        var textsForEmbedding = allChunks
            .Select(c => c.Content )
            .ToList();

        var embeddings = await _embeddingService.EmbedAsync(textsForEmbedding, ct: ct);

        var chunksWithEmbeddings = new List<ChunkWithEmbedding>(allChunks.Count);
        for (int i = 0; i < allChunks.Count; i++)
        {
            var chunk = allChunks[i];
            chunksWithEmbeddings.Add(new ChunkWithEmbedding(
                Id: chunk.Id,
                SourceId: chunk.SourceId,
                CollectionName: chunk.CollectionName,
                ChunkIndex: chunk.ChunkIndex,
                Content: chunk.Content,
                // Chunk 0 stores the document-level content hash for change detection
                ContentHash: i == 0 ? contentHash : chunk.ContentHash,
                Embedding: embeddings[i],
                PrevChunkId: i > 0 ? allChunks[i - 1].Id : null,
                NextChunkId: i < allChunks.Count - 1 ? allChunks[i + 1].Id : null));
        }

        // 7. Atomic upsert
        await _sourceWriter.UpsertSourceAsync(
            collectionName, document.SourceId, document.Title, text, chunksWithEmbeddings, ct);

        _logger.LogInformation(
            "Ingested document {SourceId}: {ChunkCount} chunks ({Action})",
            document.SourceId, allChunks.Count, isUpdate ? "updated" : "added");

        return new IngestionResult(
            Added: isUpdate ? 0 : 1,
            Updated: isUpdate ? 1 : 0,
            Deleted: 0,
            Unchanged: 0,
            sw.Elapsed,
            Failed: 0);
    }

    public async Task RemoveAsync(
        string collectionName, string sourceId, CancellationToken ct = default)
    {
        await _sourceWriter.DeleteBySourceIdAsync(collectionName, sourceId, ct);
        _logger.LogInformation("Removed document {SourceId} from {Collection}",
            sourceId, collectionName);
    }
}
