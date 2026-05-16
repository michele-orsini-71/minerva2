using Minerva.Models;

namespace Minerva.Configuration;

public sealed record MinervaIngestOptions
{
    public required string ConnectionString { get; init; }
    public required EmbeddingProviderOptions Embedding { get; init; }
    public required ChunkingOptions Chunking { get; init; }
}
