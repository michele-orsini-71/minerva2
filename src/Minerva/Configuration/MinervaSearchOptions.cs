using Minerva.Models;

namespace Minerva.Configuration;

public sealed record MinervaSearchOptions
{
    public required string ConnectionString { get; init; }
    public required EmbeddingProviderOptions Embedding { get; init; }
    public required RerankerProviderOptions? Reranker { get; init; }
    public required int TopK { get; init; }
    public required double HybridAlpha { get; init; }
    public required int CandidatePoolSize { get; init; }
    public required bool ExpandContext { get; init; }
    public required bool EnableReranker { get; init; }
}
