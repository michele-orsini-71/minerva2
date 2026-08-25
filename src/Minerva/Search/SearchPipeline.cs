using Microsoft.Extensions.Logging;
using Minerva.Ingestion;
using Minerva.Models;

namespace Minerva.Search;

class SearchPipeline
{
    private readonly IEmbeddingService _embeddingService;
    private readonly VectorSearch _vectorSearch;
    private readonly FullTextSearch _fullTextSearch;
    private readonly ContextExpander _contextExpander;
    private readonly ILogger<SearchPipeline> _logger;
    private readonly IReranker? _reranker;

    public SearchPipeline(
        IEmbeddingService embeddingService,
        VectorSearch vectorSearch,
        FullTextSearch fullTextSearch,
        ContextExpander contextExpander,
        IReranker? reranker,
        ILogger<SearchPipeline> logger)
    {
        _embeddingService = embeddingService;
        _vectorSearch = vectorSearch;
        _fullTextSearch = fullTextSearch;
        _contextExpander = contextExpander;
        _reranker = reranker;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        string collectionName,
        SearchOptions options,
        CancellationToken ct = default)
    {
        // Fusion needs the pool meaningfully deeper than TopK; otherwise each branch
        // contributes too few candidates to recover a gold ranked deep in a single
        // branch. pool < TopK is already a hard error in MinervaSearchEngine; warn when
        // the pool is less than 1.5x TopK (little headroom).
        const double minPoolRatio = 1.5;
        if (options.CandidatePoolSize < options.TopK * minPoolRatio)
            _logger.LogWarning(
                "CandidatePoolSize ({Pool}) is below {Ratio}x TopK ({TopK}); fusion has little " +
                "reranking depth. A larger pool (commonly 5-10x TopK) improves recall.",
                options.CandidatePoolSize, minPoolRatio, options.TopK);

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

        IReadOnlyList<ScoredChunk> ranked;
        if (_reranker is null || !options.EnableReranker)
        {
            ranked = fused;
        }
        else
        {
            try
            {
                ranked = await _reranker.Rank(query, fused, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "Reranker failed on collection '{Collection}'; falling back to fused ranking.",
                    collectionName);
                ranked = fused;
            }
        }
        var topK = ranked.Take(options.TopK).ToList();

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

    private static SearchResult ToSearchResult(ScoredChunk r) =>
        new(
            ChunkId: r.Chunk.Id,
            SourceId: r.Chunk.SourceId,
            CollectionName: r.Chunk.CollectionName,
            Content: r.Chunk.Content,
            Score: r.Score,
            Metadata: r.Chunk.Metadata);
}
