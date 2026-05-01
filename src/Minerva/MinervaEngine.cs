using Microsoft.Extensions.Logging;
using Minerva.Collections;
using Minerva.Exceptions;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Search;

namespace Minerva;

internal class MinervaEngine : IMinervaEngine
{
    private readonly IngestionPipeline _ingestionPipeline;
    private readonly SearchPipeline _searchPipeline;
    private readonly ICollectionService _collections;
    private readonly ILogger<MinervaEngine> _logger;

    public MinervaEngine(
        IngestionPipeline ingestionPipeline,
        SearchPipeline searchPipeline,
        ICollectionService collections,
        ILogger<MinervaEngine> logger)
    {
        _ingestionPipeline = ingestionPipeline;
        _searchPipeline = searchPipeline;
        _collections = collections;
        _logger = logger;
    }

    public ICollectionService Collections => _collections;

    public async Task<IngestionResult> IngestAsync(
        string collectionName,
        Document document,
        CancellationToken ct = default)
    {
        _ = await RequireCollectionAsync(collectionName, ct);
        return await _ingestionPipeline.IngestAsync(collectionName, document, ct);
    }

    public async Task RemoveAsync(
        string collectionName,
        string sourceId,
        CancellationToken ct = default)
    {
        _ = await RequireCollectionAsync(collectionName, ct);
        await _ingestionPipeline.RemoveAsync(collectionName, sourceId, ct);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        IReadOnlyList<string> collectionNames,
        SearchOptions? options = null,
        CancellationToken ct = default)
    {
        if (collectionNames.Count == 0)
            throw new ConfigurationException("At least one collection name must be provided.");

        foreach (var name in collectionNames)
            _ = await RequireCollectionAsync(name, ct);

        return await _searchPipeline.SearchAsync(
            query, collectionNames, options ?? new SearchOptions(), ct);
    }

    private async Task<Collection> RequireCollectionAsync(string name, CancellationToken ct)
    {
        var collection = await _collections.GetAsync(name, ct)
            ?? throw new ConfigurationException($"Collection '{name}' does not exist.");
        return collection;
    }
}
