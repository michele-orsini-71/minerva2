using Minerva.MarkdownIndexer;
using Minerva.Models;

namespace Minerva.Tests.TestSupport;

internal static class TestOptions
{
    public static ChunkingOptions Chunking(
        int targetChunkSize = 1200,
        int chunkOverlap = 200,
        int maxSegmentChars = 8000,
        ChunkerType chunkerType = ChunkerType.Custom,
        ContextualizationOptions? contextualization = null) => new()
    {
        TargetChunkSize = targetChunkSize,
        ChunkOverlap = chunkOverlap,
        MaxSegmentChars = maxSegmentChars,
        ChunkerType = chunkerType,
        Contextualization = contextualization
            ?? new ContextualizationOptions { Level = ContextualizationLevel.None },
    };

    public static EmbeddingProviderOptions Embedding(
        string baseUrl = "http://localhost:11434/v1",
        string model = "test-embedding",
        string? apiKey = null,
        int concurrency = 1,
        int batchSize = 1,
        int? requestsPerMinute = null) => new()
    {
        BaseUrl = baseUrl,
        Model = model,
        ApiKey = apiKey,
        Concurrency = concurrency,
        BatchSize = batchSize,
        RequestsPerMinute = requestsPerMinute,
    };

    public static LlmProviderOptions Llm(
        string baseUrl = "http://localhost:11434/v1",
        string model = "test-llm",
        string? apiKey = null,
        int concurrency = 1,
        int? requestsPerMinute = null) => new()
    {
        BaseUrl = baseUrl,
        Model = model,
        ApiKey = apiKey,
        Concurrency = concurrency,
        RequestsPerMinute = requestsPerMinute,
    };

    public static IndexerOptions Indexer(
        string rootPath,
        string collectionName = "test",
        IReadOnlyList<string>? excludeDirectories = null,
        IReadOnlyList<string>? fileExtensions = null,
        bool allowRecreateOnConfigMismatch = false,
        bool allowSourceScopeChange = false) => new()
    {
        RootPath = rootPath,
        CollectionName = collectionName,
        ExcludeDirectories = excludeDirectories ?? [".obsidian", ".trash", ".git"],
        FileExtensions = fileExtensions ?? ["md"],
        AllowRecreateOnConfigMismatch = allowRecreateOnConfigMismatch,
        AllowSourceScopeChange = allowSourceScopeChange,
    };

    public static CollectionProvenance Provenance(
        string embeddingModel = "test-embedding",
        int embeddingDimension = 1024,
        ChunkerType chunkerType = ChunkerType.Custom,
        int targetChunkSize = 1200,
        int chunkOverlap = 200,
        int maxSegmentChars = 8000,
        bool contextualizationEnabled = false,
        ContextualizationLevel level = ContextualizationLevel.None,
        string? contextualizationModel = null,
        string? summarizerPromptVersion = null,
        string? contextualizerPromptVersion = null,
        string ingestorVersion = "0.0.0-test",
        string schemaVersion = "003_collection_provenance") =>
        new(
            new CollectionInvariants(
                embeddingModel, embeddingDimension, chunkerType, targetChunkSize,
                chunkOverlap, maxSegmentChars, contextualizationEnabled, level,
                contextualizationModel, summarizerPromptVersion, contextualizerPromptVersion),
            new CollectionLastRun(ingestorVersion, schemaVersion));
}
