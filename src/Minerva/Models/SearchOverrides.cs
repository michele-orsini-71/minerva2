namespace Minerva.Models;

public sealed record SearchOverrides
{
    public int? TopK { get; init; }
    public double? HybridAlpha { get; init; }
    public int? CandidatePoolSize { get; init; }
    public bool? ExpandContext { get; init; }
}
