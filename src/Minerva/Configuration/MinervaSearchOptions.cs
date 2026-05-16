using Minerva.Models;

namespace Minerva.Configuration;

public sealed record MinervaSearchOptions
{
    public required string ConnectionString { get; init; }
    public required EmbeddingProviderOptions Embedding { get; init; }
}
