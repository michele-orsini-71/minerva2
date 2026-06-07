using System.Diagnostics;
using Minerva.Search.Bench.Common;
using Minerva.Search.Bench.Metrics;

namespace Minerva.Search.Bench.Sweep;

public static class SweepRunLoop
{
    public static async Task<IReadOnlyList<CellQueryResult>> RunAsync(
        ISearchEngine engine,
        string collection,
        IReadOnlyList<ParsedEntry> entries,
        IReadOnlyList<Cell> cells,
        CancellationToken ct = default)
    {
        var results = new List<CellQueryResult>(entries.Count * cells.Count);

        foreach (var cell in cells)
        {
            var overrides = CellOverrides.Build(cell);

            foreach (var entry in entries)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var hits = await engine.SearchAsync(entry.Query, collection, overrides, ct);
                    sw.Stop();

                    var gold = entry.GoldSources.ToHashSet();
                    var rankedSourceIds = hits.Select(h => h.SourceId).ToList();
                    var scores = RetrievalMetrics.Compute(rankedSourceIds, gold);

                    var retrieved = hits
                        .Select((h, i) => new RetrievedHit(
                            i + 1, h.ChunkId, h.SourceId, h.Score, gold.Contains(h.SourceId)))
                        .ToList();

                    results.Add(new CellQueryResult(
                        entry.Id, cell, scores, sw.ElapsedMilliseconds, null, retrieved));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    sw.Stop();
                    results.Add(new CellQueryResult(
                        entry.Id, cell, null, sw.ElapsedMilliseconds, ex.Message, null));
                }
            }
        }

        return results;
    }
}
