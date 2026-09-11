using Minerva.MarkdownIndexer;
using Minerva.Models;

namespace Minerva.Tests.TestSupport;

internal static class TestOptions
{
    public static ChunkingOptions Chunking(
        int targetChunkSize = 1200,
        int chunkOverlap = 200,
        ChunkerType chunkerType = ChunkerType.Custom) => new()
    {
        TargetChunkSize = targetChunkSize,
        ChunkOverlap = chunkOverlap,
        ChunkerType = chunkerType,
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
        string ingestorVersion = "0.0.0-test",
        string schemaVersion = "003_collection_provenance") =>
        new(
            new CollectionInvariants(
                embeddingModel, embeddingDimension, chunkerType, targetChunkSize, chunkOverlap),
            new CollectionLastRun(ingestorVersion, schemaVersion));
}
