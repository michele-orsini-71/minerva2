using System.Diagnostics;
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
    private readonly IChunkWriter _chunkWriter;
    private readonly string _configuredEmbeddingModel;
    private readonly IEmbeddingDimensionProvider _dimensionProvider;
    private readonly ILogger<MinervaEngine> _logger;

    public MinervaEngine(
        IngestionPipeline ingestionPipeline,
        SearchPipeline searchPipeline,
        ICollectionService collections,
        IChunkWriter chunkWriter,
        string configuredEmbeddingModel,
        IEmbeddingDimensionProvider dimensionProvider,
        ILogger<MinervaEngine> logger)
    {
        _ingestionPipeline = ingestionPipeline;
        _searchPipeline = searchPipeline;
        _collections = collections;
        _chunkWriter = chunkWriter;
        _configuredEmbeddingModel = configuredEmbeddingModel;
        _dimensionProvider = dimensionProvider;
        _logger = logger;
    }

    public ICollectionService Collections => _collections;

    public async Task<IngestionResult> IngestAsync(
        string collectionName,
        IAsyncEnumerable<Document> documents,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // Runtime guardrail: collection must match engine's configured embedder.
        var collection = await RequireCollectionAsync(collectionName, ct);
        var configuredDimension = await _dimensionProvider.GetDimensionAsync(ct);
        if (collection.EmbeddingModel != _configuredEmbeddingModel
            || collection.EmbeddingDimension != configuredDimension)
        {
            throw new CollectionEmbedderMismatchException(
                collectionName,
                collection.EmbeddingModel, collection.EmbeddingDimension,
                _configuredEmbeddingModel, configuredDimension);
        }

        var existing = await _chunkWriter.GetSourceIdsAndHashesAsync(collectionName, ct);
        var seen = new HashSet<string>();

        int added = 0, updated = 0, unchanged = 0, deleted = 0;

        await foreach (var document in documents.WithCancellation(ct))
        {
            existing.TryGetValue(document.SourceId, out var storedHash);
            var result = await _ingestionPipeline.IngestAsync(
                collectionName, document, storedHash, ct);
            added += result.Added;
            updated += result.Updated;
            unchanged += result.Unchanged;
            seen.Add(document.SourceId);
        }

        foreach (var sourceId in existing.Keys)
        {
            if (seen.Contains(sourceId)) continue;
            await _ingestionPipeline.RemoveAsync(collectionName, sourceId, ct);
            deleted++;
        }

        return new IngestionResult(added, updated, deleted, unchanged, sw.Elapsed);
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
