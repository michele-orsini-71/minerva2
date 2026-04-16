using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Minerva.Models;
using Minerva.Storage;
using Minerva.Utilities;

namespace Minerva.Ingestion;

public class IngestionPipeline
{
    private readonly DocumentChunker _chunker;
    private readonly EmbeddingService _embeddingService;
    private readonly DocumentSummarizer? _summarizer;
    private readonly ChunkContextualizer? _contextualizer;
    private readonly IChunkRepository _chunkRepository;
    private readonly ILogger<IngestionPipeline> _logger;

    public IngestionPipeline(
        DocumentChunker chunker,
        EmbeddingService embeddingService,
        DocumentSummarizer? summarizer,
        ChunkContextualizer? contextualizer,
        IChunkRepository chunkRepository,
        ILogger<IngestionPipeline> logger)
    {
        _chunker = chunker;
        _embeddingService = embeddingService;
        _summarizer = summarizer;
        _contextualizer = contextualizer;
        _chunkRepository = chunkRepository;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestAsync(
        string collectionName,
        Document document,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // 1. Integrate attachments
        var text = AttachmentIntegrator.Integrate(
            document.Text, document.Attachments ?? new Dictionary<string, AttachmentDescription>());

        // 2. Check content hash — skip if unchanged
        var contentHash = HashHelper.ComputeContentHash(text);
        var storedHash = await _chunkRepository.GetContentHashAsync(
            collectionName, document.SourceId, ct);

        if (storedHash == contentHash)
        {
            _logger.LogDebug("Document {SourceId} unchanged, skipping ingestion", document.SourceId);
            return new IngestionResult(0, 0, 0, Unchanged: 1, sw.Elapsed);
        }

        bool isUpdate = storedHash is not null;

        // 3. Segment, summarize, chunk, contextualize
        var (allChunks, contextPrefixes) = await ProcessDocumentAsync(
            collectionName, document.SourceId, text, ct);

        // 4. Apply contextual prefixes
        if (contextPrefixes is not null)
        {
            for (int i = 0; i < allChunks.Count; i++)
                allChunks[i] = allChunks[i] with { ContextualPrefix = contextPrefixes[i] };
        }

        // 5. Embed — text for embedding includes contextual prefix when present
        var textsForEmbedding = allChunks
            .Select(c => c.ContextualPrefix is not null
                ? c.ContextualPrefix + "\n" + c.Content
                : c.Content)
            .ToList();

        var embeddings = await _embeddingService.EmbedAsync(textsForEmbedding, ct: ct);

        // 6. Build ChunkWithEmbedding records with adjacency pointers
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
                ContextualPrefix: chunk.ContextualPrefix,
                PrevChunkId: i > 0 ? allChunks[i - 1].Id : null,
                NextChunkId: i < allChunks.Count - 1 ? allChunks[i + 1].Id : null));
        }

        // 7. Atomic upsert
        await _chunkRepository.UpsertChunksAsync(
            collectionName, document.SourceId, chunksWithEmbeddings, ct);

        _logger.LogInformation(
            "Ingested document {SourceId}: {ChunkCount} chunks ({Action})",
            document.SourceId, allChunks.Count, isUpdate ? "updated" : "added");

        return new IngestionResult(
            Added: isUpdate ? 0 : 1,
            Updated: isUpdate ? 1 : 0,
            Deleted: 0,
            Unchanged: 0,
            sw.Elapsed);
    }

    public async Task RemoveAsync(
        string collectionName, string sourceId, CancellationToken ct = default)
    {
        await _chunkRepository.DeleteBySourceIdAsync(collectionName, sourceId, ct);
        _logger.LogInformation("Removed document {SourceId} from {Collection}",
            sourceId, collectionName);
    }

    private async Task<(List<Chunk> chunks, IReadOnlyList<string>? contextPrefixes)>
        ProcessDocumentAsync(
            string collectionName, string sourceId, string text, CancellationToken ct)
    {
        var segments = _chunker.SegmentDocument(text);
        bool isLargeDoc = segments.Count > 1;

        if (isLargeDoc)
            return await ProcessLargeDocumentAsync(collectionName, sourceId, segments, ct);

        return await ProcessSingleDocumentAsync(collectionName, sourceId, text, ct);
    }

    private async Task<(List<Chunk>, IReadOnlyList<string>?)> ProcessSingleDocumentAsync(
        string collectionName, string sourceId, string text, CancellationToken ct)
    {
        // Summarize (optional)
        string? summary = _summarizer is not null
            ? await _summarizer.SummarizeAsync(text, ct)
            : null;

        // Chunk
        var chunks = _chunker.Chunk(collectionName, sourceId, text).ToList();

        // Contextualize (optional, requires summary)
        IReadOnlyList<string>? prefixes = null;
        if (_contextualizer is not null && summary is not null)
            prefixes = await _contextualizer.ContextualizeAsync(summary, chunks, ct);

        return (chunks, prefixes);
    }

    private async Task<(List<Chunk>, IReadOnlyList<string>?)> ProcessLargeDocumentAsync(
        string collectionName, string sourceId,
        IReadOnlyList<string> segments, CancellationToken ct)
    {
        // Summarize each segment (optional)
        IReadOnlyList<string>? summaries = _summarizer is not null
            ? await _summarizer.SummarizeSegmentsAsync(segments, ct)
            : null;

        var allChunks = new List<Chunk>();
        List<string>? allPrefixes = null;
        int startIndex = 0;

        for (int s = 0; s < segments.Count; s++)
        {
            var segChunks = _chunker.ChunkSegment(
                collectionName, sourceId, segments[s], startIndex);

            // Contextualize with this segment's summary
            if (_contextualizer is not null && summaries is not null)
            {
                var prefixes = await _contextualizer.ContextualizeAsync(
                    summaries[s], segChunks, ct);
                allPrefixes ??= new List<string>();
                allPrefixes.AddRange(prefixes);
            }

            allChunks.AddRange(segChunks);
            startIndex += segChunks.Count;
        }

        return (allChunks, allPrefixes);
    }
}
