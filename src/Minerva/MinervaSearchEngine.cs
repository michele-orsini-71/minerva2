using Minerva.Collections;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Search;

namespace Minerva;

internal sealed class MinervaSearchEngine : ISearchEngine
{
    private readonly SearchPipeline _searchPipeline;
    private readonly ICollectionService _collections;
    private readonly IChunkCatalog _chunkCatalog;
    private readonly SearchOptions _defaults;

    public MinervaSearchEngine(
        SearchPipeline searchPipeline,
        ICollectionService collections,
        IChunkCatalog chunkCatalog,
        SearchOptions defaults)
    {
        _searchPipeline = searchPipeline;
        _collections = collections;
        _chunkCatalog = chunkCatalog;
        _defaults = defaults;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        string collectionName,
        SearchOverrides? overrides = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException(
                "Collection name must be a non-empty, non-whitespace string.",
                nameof(collectionName));

        var effective = new SearchOptions
        {
            TopK = overrides?.TopK ?? _defaults.TopK,
            HybridAlpha = overrides?.HybridAlpha ?? _defaults.HybridAlpha,
            CandidatePoolSize =
                overrides?.CandidatePoolSize ?? _defaults.CandidatePoolSize,
            ExpandContext = overrides?.ExpandContext ?? _defaults.ExpandContext,
            EnableReranker = overrides?.EnableReranker ?? _defaults.EnableReranker,
        };

        if (effective.TopK <= 0)
            throw new ArgumentException(
                $"TopK must be positive (got {effective.TopK}).", nameof(overrides));
        if (effective.HybridAlpha < 0 || effective.HybridAlpha > 1)
            throw new ArgumentException(
                $"HybridAlpha must be in [0, 1] (got {effective.HybridAlpha}).", nameof(overrides));
        if (effective.CandidatePoolSize <= 0)
            throw new ArgumentException(
                $"CandidatePoolSize must be positive (got {effective.CandidatePoolSize}).",
                nameof(overrides));
        if (effective.CandidatePoolSize < effective.TopK)
            throw new ArgumentException(
                $"CandidatePoolSize must be >= TopK (got {effective.CandidatePoolSize}, TopK = {effective.TopK}).",
                nameof(overrides));

        _ = await _collections.GetAsync(collectionName, ct)
            ?? throw new ConfigurationException($"Collection '{collectionName}' does not exist.");

        return await _searchPipeline.SearchAsync(query, collectionName, effective, ct);
    }

    public async Task<bool> SourceIdExistsAsync(
        string collectionName,
        string sourceId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException(
                "Collection name must be a non-empty, non-whitespace string.",
                nameof(collectionName));
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException(
                "Source id must be a non-empty, non-whitespace string.",
                nameof(sourceId));

        _ = await _collections.GetAsync(collectionName, ct)
            ?? throw new ConfigurationException($"Collection '{collectionName}' does not exist.");

        return await _chunkCatalog.SourceIdExistsAsync(collectionName, sourceId, ct);
    }

    public async Task<Collection?> QueryCollectionInfoAsync(string collectionName, CancellationToken ct = default)
    {
        return await _collections.GetAsync(collectionName, ct);
    }
}
