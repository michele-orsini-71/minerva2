namespace Minerva.Search.Bench.Sweep;

public sealed record RawSweepConfig
{
    public string Dataset { get; init; } = "";
    public string Collection { get; init; } = "";
    public int? TopK { get; init; }
    public int? CandidatePoolSize { get; init; }
    public string? Label { get; init; }
    public Dictionary<string, List<object>> Matrix { get; init; } = new();
}
