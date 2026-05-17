using Microsoft.Extensions.Logging;
using Minerva.Ingestion;
using Minerva.Models;

namespace Minerva.Search;

public class SearchPipeline
{
    private readonly IEmbeddingService _embeddingService;
    private readonly VectorSearch _vectorSearch;
    private readonly FullTextSearch _fullTextSearch;
    private readonly ContextExpander _contextExpander;
    private readonly ILogger<SearchPipeline> _logger;

    public SearchPipeline(
        IEmbeddingService embeddingService,
        VectorSearch vectorSearch,
        FullTextSearch fullTextSearch,
        ContextExpander contextExpander,
        ILogger<SearchPipeline> logger)
    {
        _embeddingService = embeddingService;
        _vectorSearch = vectorSearch;
        _fullTextSearch = fullTextSearch;
        _contextExpander = contextExpander;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        string collectionName,
        SearchOptions options,
        CancellationToken ct = default)
    {
        var embeddings = await _embeddingService.EmbedAsync([query], ct: ct);
        var queryEmbedding = embeddings[0];

        var vectorTask = _vectorSearch.SearchAsync(
            collectionName, queryEmbedding, options.CandidatePoolSize, ct);
        var ftsTask = _fullTextSearch.SearchAsync(
            collectionName, query, options.CandidatePoolSize, ct);

        await Task.WhenAll(vectorTask, ftsTask);

        var fused = RankFusion.Fuse(
            vectorTask.Result, ftsTask.Result, options.HybridAlpha);

        _logger.LogDebug(
            "Search {Collection}: {Vector} vector + {Fts} fts → {Fused} fused",
            collectionName, vectorTask.Result.Count, ftsTask.Result.Count, fused.Count);

        var topK = fused.Take(options.TopK).ToList();

        if (topK.Count < options.TopK)
        {
            _logger.LogWarning(
                "Search returned {Actual} results, {Requested} were requested. " +
                "Collection '{Collection}' likely contains too few chunks matching the query — " +
                "broaden the query or ingest more sources.",
                topK.Count, options.TopK, collectionName);
        }

        if (options.ExpandContext)
            return await _contextExpander.ExpandAsync(topK, ct);

        return topK.Select(ToSearchResult).ToList();
    }

    private static SearchResult ToSearchResult(FusedResult r) =>
        new(
            ChunkId: r.Chunk.Id,
            SourceId: r.Chunk.SourceId,
            CollectionName: r.Chunk.CollectionName,
            Content: r.Chunk.Content,
            Score: r.Score,
            Metadata: r.Chunk.Metadata);
}
