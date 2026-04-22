using Minerva.Models;

namespace Minerva.Search;

public static class RankFusion
{
    public static IReadOnlyList<FusedResult> Fuse(
        IReadOnlyList<RankedChunk> vectorResults,
        IReadOnlyList<RankedChunk> ftsResults,
        double alpha = 0.5,
        int k = 60)
    {
        // Missing-list rank: worst possible position across the two inputs.
        int maxRank = Math.Max(vectorResults.Count, ftsResults.Count);
        int missingRank = maxRank + 1;

        var vectorByChunkId = vectorResults.ToDictionary(r => r.Chunk.Id, r => r);
        var ftsByChunkId = ftsResults.ToDictionary(r => r.Chunk.Id, r => r);

        var allIds = new HashSet<string>(vectorByChunkId.Keys);
        allIds.UnionWith(ftsByChunkId.Keys);

        var fused = new List<FusedResult>(allIds.Count);
        foreach (var id in allIds)
        {
            int vectorRank = vectorByChunkId.TryGetValue(id, out var v) ? v.Rank : missingRank;
            int ftsRank = ftsByChunkId.TryGetValue(id, out var f) ? f.Rank : missingRank;

            double score = alpha * (1.0 / (k + vectorRank))
                         + (1.0 - alpha) * (1.0 / (k + ftsRank));

            // Prefer vector chunk data when present; it carries the same fields either way.
            ChunkSearchRecord chunk = vectorByChunkId.TryGetValue(id, out var vr)
                ? vr.Chunk
                : ftsByChunkId[id].Chunk;

            fused.Add(new FusedResult(chunk, score));
        }

        fused.Sort((a, b) => b.Score.CompareTo(a.Score));
        return fused;
    }
}
