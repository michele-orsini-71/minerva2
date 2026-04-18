namespace Minerva.Configuration;

public class MinervaOptions
{
    public required string ConnectionString { get; set; }
    public required ProviderOptions Embedding { get; set; }
    public ProviderOptions? Llm { get; set; }
    public ChunkingOptions Chunking { get; set; } = new();
}

public class ProviderOptions
{
    public required string BaseUrl { get; set; }
    public required string Model { get; set; }
    public string? ApiKey { get; set; }
    public int? RequestsPerMinute { get; set; }
    public int Concurrency { get; set; } = 1;
    public int BatchSize { get; set; } = 1;
}

public class ChunkingOptions
{
    public int TargetChunkSize { get; set; } = 1200;
    public int ChunkOverlap { get; set; } = 200;
    public bool EnableSummarization { get; set; }
    public bool EnableContextualization { get; set; }
    public int LargeDocumentThreshold { get; set; } = 8000;
}
