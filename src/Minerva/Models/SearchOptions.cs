namespace Minerva.Models;

public sealed record SearchOptions
{
    public required int TopK { get; init; }
    public required double HybridAlpha { get; init; }
    public required bool ExpandContext { get; init; }
    public required int CandidatePoolSize { get; init; }
}
