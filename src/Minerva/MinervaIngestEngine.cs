using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Minerva.Collections;
using Minerva.Exceptions;
using Minerva.Ingestion;
using Minerva.Models;

namespace Minerva;

internal sealed class MinervaIngestEngine : IIngestEngine
{
    private readonly IngestionPipeline _ingestionPipeline;
    private readonly ICollectionService _collections;
    private readonly IChunkWriter _chunkWriter;
    private readonly string _configuredEmbeddingModel;
    private readonly IEmbeddingDimensionProvider _dimensionProvider;
    private readonly ChunkingOptions _chunking;
    private readonly string _schemaVersion;
    private readonly ILogger<MinervaIngestEngine> _logger;

    public MinervaIngestEngine(
        IngestionPipeline ingestionPipeline,
        ICollectionService collections,
        IChunkWriter chunkWriter,
        string configuredEmbeddingModel,
        IEmbeddingDimensionProvider dimensionProvider,
        ChunkingOptions chunking,
        string schemaVersion,
        ILogger<MinervaIngestEngine> logger)
    {
        _ingestionPipeline = ingestionPipeline;
        _collections = collections;
        _chunkWriter = chunkWriter;
        _configuredEmbeddingModel = configuredEmbeddingModel;
        _dimensionProvider = dimensionProvider;
        _chunking = chunking;
        _schemaVersion = schemaVersion;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestAsync(
        string collectionName,
        ClientProvenance clientProvenance,
        IAsyncEnumerable<Document> documents,
        bool allowRecreateOnConfigMismatch = false,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        await PrepareCollectionAsync(collectionName, clientProvenance, allowRecreateOnConfigMismatch, ct);

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

    public async Task<Collection?> QueryCollectionInfoAsync(string collectionName, CancellationToken ct = default)
    {
        return await _collections.GetAsync(collectionName, ct);
    }
    
    private async Task PrepareCollectionAsync(string collectionName, ClientProvenance clientProvenance, bool allowRecreateOnConfigMismatch, CancellationToken ct)
    {
        var provenance = await BuildProvenanceAsync(ct);
        var existing = await _collections.GetAsync(collectionName, ct);

        if (existing is null)
        {
            await _collections.EnsureAsync(collectionName, provenance, clientProvenance, description: null, ct);
            return;
        }

        var drifts = DiffInvariants(existing.Provenance.Invariants, provenance.Invariants);
        if (drifts.Count == 0)
            return;

        if (!allowRecreateOnConfigMismatch)
            throw new CollectionConfigMismatchException(collectionName, drifts);

        _logger.LogWarning(
            "Collection '{Collection}' config mismatch and AllowRecreateOnConfigMismatch=true; dropping all data and recreating. Drifted: {Drifted}",
            collectionName, string.Join(", ", drifts.Select(d => d.Field)));
        await _collections.DeleteAsync(collectionName, ct);
        await _collections.EnsureAsync(collectionName, provenance, clientProvenance, description: null, ct);
    }

    private static List<InvariantDrift> DiffInvariants(
        CollectionInvariants stored, CollectionInvariants configured)
    {
        var drifts = new List<InvariantDrift>();

        void Compare(string field, object? a, object? b)
        {
            if (!Equals(a, b))
                drifts.Add(new InvariantDrift(field, a?.ToString(), b?.ToString()));
        }

        Compare("embeddingModel", stored.EmbeddingModel, configured.EmbeddingModel);
        Compare("embeddingDimension", stored.EmbeddingDimension, configured.EmbeddingDimension);
        Compare("chunkerType", stored.ChunkerType, configured.ChunkerType);
        Compare("targetChunkSize", stored.TargetChunkSize, configured.TargetChunkSize);
        Compare("chunkOverlap", stored.ChunkOverlap, configured.ChunkOverlap);
        Compare("maxSegmentChars", stored.MaxSegmentChars, configured.MaxSegmentChars);
        Compare("contextualizationLevel", stored.Level, configured.Level);
        Compare("contextualizationModel", stored.ContextualizationModel, configured.ContextualizationModel);
        Compare("summarizerPromptVersion", stored.SummarizerPromptVersion, configured.SummarizerPromptVersion);
        Compare("contextualizerPromptVersion", stored.ContextualizerPromptVersion, configured.ContextualizerPromptVersion);

        return drifts;
    }

    private async Task<CollectionProvenance> BuildProvenanceAsync(CancellationToken ct)
    {
        var dimension = await _dimensionProvider.GetDimensionAsync(ct);
        var level = _chunking.Contextualization.Level;

        var invariants = new CollectionInvariants(
            EmbeddingModel: _configuredEmbeddingModel,
            EmbeddingDimension: dimension,
            ChunkerType: _chunking.ChunkerType,
            TargetChunkSize: _chunking.TargetChunkSize,
            ChunkOverlap: _chunking.ChunkOverlap,
            MaxSegmentChars: _chunking.MaxSegmentChars,
            ContextualizationEnabled: level != ContextualizationLevel.None,
            Level: level,
            ContextualizationModel: _chunking.Contextualization.Llm?.Model,
            SummarizerPromptVersion: level >= ContextualizationLevel.DocumentBrief ? DocumentSummarizer.PromptVersion : null,
            ContextualizerPromptVersion: level == ContextualizationLevel.PerChunk ? ChunkContextualizer.PromptVersion : null);

        var lastRun = new CollectionLastRun(
            IngestorVersion: IngestorVersion,
            SchemaVersion: _schemaVersion);

        return new CollectionProvenance(invariants, lastRun);
    }

    private static string IngestorVersion =>
        typeof(MinervaIngestEngine).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}
