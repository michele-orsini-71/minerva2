namespace Minerva.Models;

public class ChunkingOptions
{
    public int TargetChunkSize { get; set; } = 1200;
    public int ChunkOverlap { get; set; } = 200;
    public bool EnableSummarization { get; set; }
    public bool EnableContextualization { get; set; }
    public int LargeDocumentThreshold { get; set; } = 8000;
}
