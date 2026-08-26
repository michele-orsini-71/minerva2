using Minerva.Search.Bench.Sweep;

namespace Minerva.Tests.Search.Bench;


[Trait("Category", "Bench")]
public class CellOverridesTests
{
    private static SweepConfig Config() => new(
        "d", "c", 10, 100, null, new Dictionary<string, List<object>>());

    private static Cell Cell(params (string Knob, object Value)[] pairs)
        => new(pairs.Select(p => new KeyValuePair<string, object>(p.Knob, p.Value)).ToList());

    [Fact]
    public void FixedParams_ComeFromConfig_KnobsNullWhenAbsent()
    {
        var overrides = CellOverrides.Build(Cell(), Config());

        Assert.Equal(10, overrides.TopK);
        Assert.Equal(100, overrides.CandidatePoolSize);
        Assert.Null(overrides.HybridAlpha);
        Assert.Null(overrides.EnableReranker);
    }

    [Fact]
    public void EnableReranker_MapsToBool()
    {
        var overrides = CellOverrides.Build(Cell(("enable_reranker", false)), Config());

        Assert.False(overrides.EnableReranker);
    }

    [Fact]
    public void HybridAlpha_MapsToDouble()
    {
        var overrides = CellOverrides.Build(Cell(("hybrid_alpha", 0.5)), Config());

        Assert.Equal(0.5, overrides.HybridAlpha);
    }

    [Fact]
    public void BothKnobs_MapTogether()
    {
        var overrides = CellOverrides.Build(
            Cell(("enable_reranker", true), ("hybrid_alpha", 0.3)), Config());

        Assert.Equal(true, overrides.EnableReranker);
        Assert.Equal(0.3, overrides.HybridAlpha);
    }

    [Fact]
    public void UnsupportedKnob_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CellOverrides.Build(Cell(("top_k", 10L)), Config()));
    }
}
