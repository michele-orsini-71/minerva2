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

    public async Task<IngestionResult> IngestAsync(
        string collectionName,
        IAsyncEnumerable<Document> documents,
        bool forceRecreate = false,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        await PrepareCollectionAsync(collectionName, forceRecreate, ct);

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
            deleted += result.Deleted;
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

    private async Task PrepareCollectionAsync(string collectionName, bool forceRecreate, CancellationToken ct)
    {
        var existing = await _collections.GetAsync(collectionName, ct);

        if (existing is null)
        {
            await _collections.EnsureAsync(collectionName, description: null, metadata: null, ct);
            return;
        }

        var configuredDimension = await _dimensionProvider.GetDimensionAsync(ct);
        if (existing.EmbeddingModel == _configuredEmbeddingModel
            && existing.EmbeddingDimension == configuredDimension)
        {
            return;
        }

        if (!forceRecreate)
        {
            throw new CollectionEmbedderMismatchException(
                collectionName,
                existing.EmbeddingModel, existing.EmbeddingDimension,
                _configuredEmbeddingModel, configuredDimension);
        }

        _logger.LogWarning(
            "Collection '{Collection}' embedder mismatch and forceRecreate=true; dropping all data and recreating",
            collectionName);
        await _collections.DeleteAsync(collectionName, ct);
        await _collections.EnsureAsync(collectionName, description: null, metadata: null, ct);
    }
}
