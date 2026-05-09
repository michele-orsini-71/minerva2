namespace Minerva.Models;

public enum ChunkerType
{
    Custom,
    SemanticKernel,
}

public class ChunkingOptions
{
    public int TargetChunkSize { get; set; } = 1200;
    public int ChunkOverlap { get; set; } = 200;
    public bool EnableSummarization { get; set; }
    public bool EnableContextualization { get; set; }
    public int MaxSegmentChars { get; set; } = 8000;
    public ChunkerType ChunkerType { get; set; } = ChunkerType.Custom;
}
