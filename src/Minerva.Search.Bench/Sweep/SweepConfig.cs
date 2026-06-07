namespace Minerva.Search.Bench.Sweep;

public sealed record SweepConfig
{
    public string Dataset { get; init; } = "";
    public string Collection { get; init; } = "";
    public Dictionary<string, List<object>> Matrix { get; init; } = new();
}
