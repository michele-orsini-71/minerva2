namespace Minerva.Models;

public sealed record ContextualizationOptions
{
    public required ContextualizationLevel Level { get; init; }
    public LlmProviderOptions? Llm { get; init; }
}