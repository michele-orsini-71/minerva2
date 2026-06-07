namespace Minerva.Search.Bench.Metrics;

public sealed record MetricScores(
    double RecallAt5,
    double RecallAt10,
    double RecallAt20,
    double MrrAt10);

public static class RetrievalMetrics
{
    public static MetricScores Compute(
        IReadOnlyList<string> rankedSourceIds,
        IReadOnlySet<string> goldSources)
    {
        if (goldSources.Count == 0)
            throw new ArgumentException("goldSources must be non-empty.", nameof(goldSources));

        return new(
            RecallAt(rankedSourceIds, goldSources, 5),
            RecallAt(rankedSourceIds, goldSources, 10),
            RecallAt(rankedSourceIds, goldSources, 20),
            MrrAt(rankedSourceIds, goldSources, 10));
    }

    // Recall@K = fraction of gold sources that appear within the first K results.
    private static double RecallAt(
        IReadOnlyList<string> ranked, IReadOnlySet<string> gold, int k)
    {
        var topK = ranked.Take(k).ToHashSet();
        var hits = gold.Count(topK.Contains);
        return (double)hits / gold.Count;
    }

    // MRR@K = reciprocal rank of the first gold hit within the first K results, else 0.
    private static double MrrAt(
        IReadOnlyList<string> ranked, IReadOnlySet<string> gold, int k)
    {
        var limit = Math.Min(k, ranked.Count);
        for (int i = 0; i < limit; i++)
            if (gold.Contains(ranked[i]))
                return 1.0 / (i + 1);

        return 0.0;
    }
}
