using Minerva.Models;
using Minerva.Search;

namespace Minerva.Tests.Search;

[Trait("Category", "Search")]
public class RankFusionTests
{
    private static RankedChunk MakeRanked(string id, int rank) =>
        new(new ChunkSearchRecord(
            Id: id,
            SourceId: $"src-{id}",
            CollectionName: "c",
            ChunkIndex: 0,
            Content: id,
            RawScore: 0.0), rank);

    [Fact]
    public void Fuse_DisjointLists_MergesByRrfScore()
    {
        var vector = new[] { MakeRanked("a", 1), MakeRanked("b", 2) };
        var fts = new[] { MakeRanked("c", 1), MakeRanked("d", 2) };

        var fused = RankFusion.Fuse(vector, fts);

        Assert.Equal(4, fused.Count);
        // a and c both have rank 1 in their respective lists; scores must be equal, top-ranked
        var topTwo = fused.Take(2).Select(r => r.Chunk.Id).ToHashSet();
        Assert.Contains("a", topTwo);
        Assert.Contains("c", topTwo);
    }

    [Fact]
    public void Fuse_OverlappingLists_SharedItemsRankHigher()
    {
        // "shared" is rank 1 in both lists; "v-only" is rank 1 only in vector; "f-only" is rank 1 only in fts.
        // Shared gets contributions from both sides so it wins.
        var vector = new[] { MakeRanked("shared", 1), MakeRanked("v-only", 2) };
        var fts = new[] { MakeRanked("shared", 1), MakeRanked("f-only", 2) };

        var fused = RankFusion.Fuse(vector, fts);

        Assert.Equal(3, fused.Count);
        Assert.Equal("shared", fused[0].Chunk.Id);
    }

    [Fact]
    public void Fuse_AlphaOne_PreservesVectorOrder()
    {
        var vector = new[] { MakeRanked("v1", 1), MakeRanked("v2", 2), MakeRanked("v3", 3) };
        var fts = new[] { MakeRanked("f1", 1), MakeRanked("f2", 2) };

        var fused = RankFusion.Fuse(vector, fts, alpha: 1.0);

        // With alpha=1.0, the FTS contribution drops out. Vector items retain their rank order.
        var vectorOrder = fused
            .Where(r => r.Chunk.Id.StartsWith("v"))
            .Select(r => r.Chunk.Id)
            .ToList();
        Assert.Equal(["v1", "v2", "v3"], vectorOrder);

        // Vector items should each outrank FTS-only items (which have worst vector rank).
        double lowestVectorScore = fused.Where(r => r.Chunk.Id.StartsWith("v")).Min(r => r.Score);
        double highestFtsScore = fused.Where(r => r.Chunk.Id.StartsWith("f")).Max(r => r.Score);
        Assert.True(lowestVectorScore >= highestFtsScore);
    }

    [Fact]
    public void Fuse_AlphaZero_PreservesFtsOrder()
    {
        var vector = new[] { MakeRanked("v1", 1), MakeRanked("v2", 2) };
        var fts = new[] { MakeRanked("f1", 1), MakeRanked("f2", 2), MakeRanked("f3", 3) };

        var fused = RankFusion.Fuse(vector, fts, alpha: 0.0);

        var ftsOrder = fused
            .Where(r => r.Chunk.Id.StartsWith("f"))
            .Select(r => r.Chunk.Id)
            .ToList();
        Assert.Equal(["f1", "f2", "f3"], ftsOrder);
    }

    [Fact]
    public void Fuse_BothEmpty_ReturnsEmpty()
    {
        var fused = RankFusion.Fuse([], []);
        Assert.Empty(fused);
    }

    [Fact]
    public void Fuse_OneEmpty_ReturnsOtherInRankOrder()
    {
        var vector = new[] { MakeRanked("v1", 1), MakeRanked("v2", 2) };

        var fused = RankFusion.Fuse(vector, []);

        Assert.Equal(2, fused.Count);
        Assert.Equal("v1", fused[0].Chunk.Id);
        Assert.Equal("v2", fused[1].Chunk.Id);
    }
}
