namespace Minerva.Configuration;

public class MinervaOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public ProviderOptions Embedding { get; set; } = new();
    public ProviderOptions? Llm { get; set; }
    public ChunkingOptions Chunking { get; set; } = new();
}

public class ProviderOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
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
