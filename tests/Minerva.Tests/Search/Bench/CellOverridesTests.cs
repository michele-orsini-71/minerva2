using Minerva.Search.Bench.Sweep;

namespace Minerva.Tests.Search.Bench;


[Trait("Category", "Bench")]
public class CellOverridesTests
{
    private static Cell Cell(params (string Knob, object Value)[] pairs)
        => new(pairs.Select(p => new KeyValuePair<string, object>(p.Knob, p.Value)).ToList());

    [Fact]
    public void TopK_MapsToInt_HybridAlphaNull()
    {
        var overrides = CellOverrides.Build(Cell(("top_k", 10L)));

        Assert.Equal(10, overrides.TopK);
        Assert.Null(overrides.HybridAlpha);
    }

    [Fact]
    public void HybridAlpha_MapsToDouble_TopKNull()
    {
        var overrides = CellOverrides.Build(Cell(("hybrid_alpha", 0.5)));

        Assert.Equal(0.5, overrides.HybridAlpha);
        Assert.Null(overrides.TopK);
    }

    [Fact]
    public void BothKnobs_MapTogether()
    {
        var overrides = CellOverrides.Build(Cell(("top_k", 20L), ("hybrid_alpha", 0.3)));

        Assert.Equal(20, overrides.TopK);
        Assert.Equal(0.3, overrides.HybridAlpha);
    }

    [Fact]
    public void UnsupportedKnob_Throws()
    {
        Assert.Throws<ArgumentException>(() => CellOverrides.Build(Cell(("bogus", 1L))));
    }
}
