using Minerva.Collections;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Search;

namespace Minerva;

internal sealed class MinervaSearchEngine : ISearchEngine
{
    private readonly SearchPipeline _searchPipeline;
    private readonly ICollectionService _collections;
    private readonly SearchOptions _defaults;

    public MinervaSearchEngine(
        SearchPipeline searchPipeline,
        ICollectionService collections,
        SearchOptions defaults)
    {
        _searchPipeline = searchPipeline;
        _collections = collections;
        _defaults = defaults;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        IReadOnlyList<string> collectionNames,
        SearchOverrides? overrides = null,
        CancellationToken ct = default)
    {
        var effective = new SearchOptions
        {
            TopK = overrides?.TopK ?? _defaults.TopK,
            HybridAlpha = overrides?.HybridAlpha ?? _defaults.HybridAlpha,
            CandidatePoolMultiplier =
                overrides?.CandidatePoolMultiplier ?? _defaults.CandidatePoolMultiplier,
            ExpandContext = overrides?.ExpandContext ?? _defaults.ExpandContext,
        };

        if (effective.TopK <= 0)
            throw new ArgumentException(
                $"TopK must be positive (got {effective.TopK}).", nameof(overrides));
        if (effective.HybridAlpha < 0 || effective.HybridAlpha > 1)
            throw new ArgumentException(
                $"HybridAlpha must be in [0, 1] (got {effective.HybridAlpha}).", nameof(overrides));
        if (effective.CandidatePoolMultiplier <= 0)
            throw new ArgumentException(
                $"CandidatePoolMultiplier must be positive (got {effective.CandidatePoolMultiplier}).",
                nameof(overrides));

        if (collectionNames.Count == 0)
            throw new ConfigurationException("At least one collection name must be provided.");

        foreach (var name in collectionNames)
        {
            _ = await _collections.GetAsync(name, ct)
                ?? throw new ConfigurationException($"Collection '{name}' does not exist.");
        }

        return await _searchPipeline.SearchAsync(query, collectionNames, effective, ct);
    }
}
