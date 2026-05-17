namespace Minerva.Models;

public sealed record SearchOverrides
{
    public int? TopK { get; init; }
    public double? HybridAlpha { get; init; }
    public int? CandidatePoolMultiplier { get; init; }
    public bool? ExpandContext { get; init; }
}
