using Minerva.Models;

namespace Minerva.Configuration;

public class MinervaOptions
{
    public string? ConnectionString { get; set; }
    public required ProviderOptions Embedding { get; set; }
    public ProviderOptions? Llm { get; set; }
    public ChunkingOptions Chunking { get; set; } = new();
}
