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
        IReadOnlyList<string> collectionNames,
        SearchOptions options,
        CancellationToken ct = default)
    {
        // 1. Embed the query once; reuse across all collections.
        var embeddings = await _embeddingService.EmbedAsync([query], ct: ct);
        var queryEmbedding = embeddings[0];

        // 2. Per collection: vector + FTS in parallel, fuse.
        var perCollectionTasks = collectionNames
            .Select(c => SearchCollectionAsync(c, query, queryEmbedding, options, ct))
            .ToList();

        var perCollection = await Task.WhenAll(perCollectionTasks);

        // 3. Merge fused results across collections by score, take top K.
        var merged = perCollection
            .SelectMany(r => r)
            .OrderByDescending(r => r.Score)
            .Take(options.TopK)
            .ToList();

        if (merged.Count < options.TopK)
        {
            _logger.LogWarning(
                "Search returned {Actual} results, {Requested} were requested. " +
                "Candidate pool may be too small for this corpus — consider increasing CandidatePoolMultiplier (currently {Multiplier}).",
                merged.Count, options.TopK, options.CandidatePoolMultiplier);
        }

        // 4. Optional context expansion.
        if (options.ExpandContext)
            return await _contextExpander.ExpandAsync(merged, ct);

        return merged.Select(ToSearchResult).ToList();
    }

    private async Task<IReadOnlyList<FusedResult>> SearchCollectionAsync(
        string collectionName,
        string query,
        float[] queryEmbedding,
        SearchOptions options,
        CancellationToken ct)
    {
        var candidatePoolSize = options.TopK * options.CandidatePoolMultiplier;

        var vectorTask = _vectorSearch.SearchAsync(
            collectionName, queryEmbedding, candidatePoolSize, ct);
        var ftsTask = _fullTextSearch.SearchAsync(
            collectionName, query, candidatePoolSize, ct);

        await Task.WhenAll(vectorTask, ftsTask);

        var fused = RankFusion.Fuse(
            vectorTask.Result, ftsTask.Result, options.HybridAlpha);

        // Dedup by SourceId — fused is already sorted desc by score,
        // so the first occurrence of each source is the highest-scoring chunk.
        var seen = new HashSet<string>();
        var dedupedBySource = new List<FusedResult>();
        foreach (var f in fused)
        {
            if (seen.Add(f.Chunk.SourceId))
                dedupedBySource.Add(f);
        }

        _logger.LogDebug(
            "Search {Collection}: {Vector} vector + {Fts} fts → {Fused} fused → {Sources} sources",
            collectionName, vectorTask.Result.Count, ftsTask.Result.Count,
            fused.Count, dedupedBySource.Count);

        return dedupedBySource.Take(options.TopK).ToList();
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
