using Minerva.Search.Bench.Sweep;

namespace Minerva.Tests.Search.Bench;


[Trait("Category", "Bench")]
public class CellEnumeratorTests
{
    [Fact]
    public void OneByOne_SingleCell()
    {
        var matrix = new Dictionary<string, List<object>> { ["top_k"] = [10] };

        var cells = CellEnumerator.Enumerate(matrix);

        var cell = Assert.Single(cells);
        var kv = Assert.Single(cell.Values);
        Assert.Equal("top_k", kv.Key);
        Assert.Equal(10, kv.Value);
    }

    [Fact]
    public void OneByThree_ThreeCells()
    {
        var matrix = new Dictionary<string, List<object>> { ["top_k"] = [10, 20, 30] };

        var cells = CellEnumerator.Enumerate(matrix);

        Assert.Equal(3, cells.Count);
        Assert.Equal([10, 20, 30], cells.Select(c => c.Values[0].Value));
    }

    [Fact]
    public void TwoByThree_SixCells_CartesianProduct()
    {
        var matrix = new Dictionary<string, List<object>>
        {
            ["top_k"] = [10, 20],
            ["hybrid_alpha"] = [0.3, 0.5, 0.7],
        };

        var cells = CellEnumerator.Enumerate(matrix);

        Assert.Equal(6, cells.Count);
        var combos = cells
            .Select(c => (c.Values[0].Value, c.Values[1].Value))
            .ToList();
        Assert.Contains(((object)10, (object)0.3), combos);
        Assert.Contains(((object)20, (object)0.7), combos);
    }

    [Fact]
    public void KnobOrder_IsStable_AcrossAllCells()
    {
        var matrix = new Dictionary<string, List<object>>
        {
            ["top_k"] = [10, 20],
            ["hybrid_alpha"] = [0.3, 0.5],
        };

        var cells = CellEnumerator.Enumerate(matrix);

        Assert.All(cells, c =>
        {
            Assert.Equal("top_k", c.Values[0].Key);
            Assert.Equal("hybrid_alpha", c.Values[1].Key);
        });
    }
}
