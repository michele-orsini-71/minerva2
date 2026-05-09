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
    public required ContextBudgetOptions ContextBudget { get; init; }
    public required ChunkerType ChunkerType { get; init; }
    public LlmProviderOptions? Llm { get; init; }
}
