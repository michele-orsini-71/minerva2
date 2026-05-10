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
        LlmProviderOptions? llm = null) => new()
    {
        TargetChunkSize = targetChunkSize,
        ChunkOverlap = chunkOverlap,
        MaxSegmentChars = maxSegmentChars,
        ChunkerType = chunkerType,
        Llm = llm,
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
        IReadOnlyList<string>? excludeDirectories = null) => new()
    {
        RootPath = rootPath,
        CollectionName = collectionName,
        ExcludeDirectories = excludeDirectories ?? [".obsidian", ".trash", ".git"],
    };
}
