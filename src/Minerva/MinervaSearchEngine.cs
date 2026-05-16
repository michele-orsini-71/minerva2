using Minerva.Collections;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Search;

namespace Minerva;

internal sealed class MinervaSearchEngine : ISearchEngine
{
    private readonly SearchPipeline _searchPipeline;
    private readonly ICollectionService _collections;

    public MinervaSearchEngine(
        SearchPipeline searchPipeline,
        ICollectionService collections)
    {
        _searchPipeline = searchPipeline;
        _collections = collections;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        IReadOnlyList<string> collectionNames,
        SearchOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TopK <= 0)
            throw new ArgumentException(
                $"TopK must be positive (got {options.TopK}).", nameof(options));
        if (options.HybridAlpha < 0 || options.HybridAlpha > 1)
            throw new ArgumentException(
                $"HybridAlpha must be in [0, 1] (got {options.HybridAlpha}).", nameof(options));
        if (options.CandidatePoolMultiplier <= 0)
            throw new ArgumentException(
                $"CandidatePoolMultiplier must be positive (got {options.CandidatePoolMultiplier}).",
                nameof(options));

        if (collectionNames.Count == 0)
            throw new ConfigurationException("At least one collection name must be provided.");

        foreach (var name in collectionNames)
        {
            _ = await _collections.GetAsync(name, ct)
                ?? throw new ConfigurationException($"Collection '{name}' does not exist.");
        }

        return await _searchPipeline.SearchAsync(query, collectionNames, options, ct);
    }
}
