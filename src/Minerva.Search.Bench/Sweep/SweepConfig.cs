namespace Minerva.Search.Bench.Sweep;

public sealed record SweepConfig(string Dataset,
    string Collection, int TopK, int CandidatePoolSize, string? Label, Dictionary<string, List<object>> Matrix);