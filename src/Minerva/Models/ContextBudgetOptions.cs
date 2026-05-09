namespace Minerva.Models;

public sealed record ContextBudgetOptions
{
    public required int MaxContextTokens { get; init; }
    public required int ReservedTokens { get; init; }
    public required double CharsPerToken { get; init; }
    public required double SafetyFactor { get; init; }
}
