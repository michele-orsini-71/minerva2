using Minerva.Search.Bench.Metrics;

namespace Minerva.Tests.Search.Bench;


[Trait("Category", "Bench")]
public class RetrievalMetricsTests
{
    private static IReadOnlySet<string> Gold(params string[] ids) => ids.ToHashSet();

    // ranked list of N source ids "s1".."sN", with the gold source placed at rank `goldRank` (1-based)
    private static List<string> RankedWithGoldAt(int goldRank, int length, string gold)
    {
        var ranked = new List<string>();
        for (int rank = 1; rank <= length; rank++)
            ranked.Add(rank == goldRank ? gold : $"s{rank}");
        return ranked;
    }

    [Fact]
    public void EmptyGold_Throws()
    {
        var ranked = Enumerable.Range(1, 20).Select(i => $"s{i}").ToList();

        Assert.Throws<ArgumentException>(() => RetrievalMetrics.Compute(ranked, Gold()));
    }

    [Fact]
    public void NoHit_AllZero()
    {
        var ranked = Enumerable.Range(1, 20).Select(i => $"s{i}").ToList();

        var m = RetrievalMetrics.Compute(ranked, Gold("gold"));

        Assert.Equal(0.0, m.RecallAt5);
        Assert.Equal(0.0, m.RecallAt10);
        Assert.Equal(0.0, m.RecallAt20);
        Assert.Equal(0.0, m.MrrAt10);
    }

    [Fact]
    public void HitAtRank1()
    {
        var ranked = RankedWithGoldAt(1, 20, "gold");

        var m = RetrievalMetrics.Compute(ranked, Gold("gold"));

        Assert.Equal(1.0, m.RecallAt5);
        Assert.Equal(1.0, m.RecallAt10);
        Assert.Equal(1.0, m.RecallAt20);
        Assert.Equal(1.0, m.MrrAt10);
    }

    [Fact]
    public void HitAtRank5()
    {
        var ranked = RankedWithGoldAt(5, 20, "gold");

        var m = RetrievalMetrics.Compute(ranked, Gold("gold"));

        Assert.Equal(1.0, m.RecallAt5);
        Assert.Equal(1.0, m.RecallAt10);
        Assert.Equal(0.2, m.MrrAt10, 5);
    }

    [Fact]
    public void HitAtRank10_NotInTop5()
    {
        var ranked = RankedWithGoldAt(10, 20, "gold");

        var m = RetrievalMetrics.Compute(ranked, Gold("gold"));

        Assert.Equal(0.0, m.RecallAt5);
        Assert.Equal(1.0, m.RecallAt10);
        Assert.Equal(1.0, m.RecallAt20);
        Assert.Equal(0.1, m.MrrAt10, 5);
    }

    [Fact]
    public void HitAtRank11_OutsideTop10()
    {
        var ranked = RankedWithGoldAt(11, 20, "gold");

        var m = RetrievalMetrics.Compute(ranked, Gold("gold"));

        Assert.Equal(0.0, m.RecallAt5);
        Assert.Equal(0.0, m.RecallAt10);
        Assert.Equal(1.0, m.RecallAt20);
        Assert.Equal(0.0, m.MrrAt10); // first hit at rank 11 is beyond MRR@10
    }

    [Fact]
    public void MultipleGold_PartialThenFull()
    {
        // gold A at rank 1, gold B at rank 8
        var ranked = new List<string> { "A", "s2", "s3", "s4", "s5", "s6", "s7", "B", "s9", "s10" };

        var m = RetrievalMetrics.Compute(ranked, Gold("A", "B"));

        Assert.Equal(0.5, m.RecallAt5, 5);  // only A within top 5
        Assert.Equal(1.0, m.SuccessAt5, 5);  // A within top 5 means Success = 1.0
        Assert.Equal(1.0, m.RecallAt10, 5); // both within top 10
        Assert.Equal(1.0, m.SuccessAt10, 5); // both within top 10 means Success = 1.0 again
        Assert.Equal(1.0, m.MrrAt10, 5);    // first gold hit is A at rank 1
    }

    [Fact]
    public void TopKShorterThan20()
    {
        // only 6 results; gold at rank 3
        var ranked = RankedWithGoldAt(3, 6, "gold");

        var m = RetrievalMetrics.Compute(ranked, Gold("gold"));

        Assert.Equal(1.0, m.RecallAt5);
        Assert.Equal(1.0, m.RecallAt10);
        Assert.Equal(1.0, m.RecallAt20);
        Assert.Equal(1.0 / 3, m.MrrAt10, 5);
    }
}
