namespace Minerva.Models;

public enum ChunkerType
{
    Custom,
    SemanticKernel,
}

public sealed record ChunkingOptions
{
    public required int TargetChunkSize { get; init; }
    public required int ChunkOverlap { get; init; }
    public required int MaxSegmentChars { get; init; }
    public required ChunkerType ChunkerType { get; init; }
    public required ContextualizationOptions Contextualization { get; init; }
}
