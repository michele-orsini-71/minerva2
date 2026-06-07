using Minerva.Search.Bench.Metrics;

namespace Minerva.Search.Bench.Sweep;

public sealed record CellQueryResult(
    string QueryId,
    Cell Cell,
    MetricScores? Scores,
    long LatencyMs,
    string? Error,
    IReadOnlyList<RetrievedHit>? Hits);

public sealed record RetrievedHit(
    int Rank,
    string ChunkId,
    string SourceId,
    double Score,
    bool GoldHit);
