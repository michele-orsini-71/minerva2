namespace Minerva.Search.Bench.Metrics;

public sealed record MetricScores(
    double RecallAt5,
    double RecallAt10,
    double RecallAt20,
    double SuccessAt5,
    double SuccessAt10,
    double SuccessAt20,
    double RrAt10
);

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
            SuccessAt(rankedSourceIds, goldSources, 5),
            SuccessAt(rankedSourceIds, goldSources, 10),
            SuccessAt(rankedSourceIds, goldSources, 20),
            RrAt(rankedSourceIds, goldSources, 10));
    }

    // Recall@K = fraction of gold sources that appear within the first K results.
    private static double RecallAt(
        IReadOnlyList<string> ranked, IReadOnlySet<string> gold, int k)
    {
        var topK = ranked.Take(k).ToHashSet();
        var hits = gold.Count(topK.Contains);
        return (double)hits / gold.Count;
    }

    // Success@K = 1.0 if any gold source appears within the first K results, 0.0 otherwise
    private static double SuccessAt(
        IReadOnlyList<string> ranked, IReadOnlySet<string> gold, int k)
    {
        return ranked.Take(k).Any(gold.Contains) ? 1.0 : 0.0;
    }

    // RR@K = reciprocal rank of the first gold hit within the first K results, else 0.
    // Aggregated as a mean over queries (MRR@K) at report time, not here.
    private static double RrAt(
        IReadOnlyList<string> ranked, IReadOnlySet<string> gold, int k)
    {
        var limit = Math.Min(k, ranked.Count);
        for (int i = 0; i < limit; i++)
            if (gold.Contains(ranked[i]))
                return 1.0 / (i + 1);

        return 0.0;
    }
}
