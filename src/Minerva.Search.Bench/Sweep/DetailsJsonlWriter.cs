using System.Text.Json;
using Minerva.Search.Bench.Common;

namespace Minerva.Search.Bench.Sweep;

public static class DetailsJsonlWriter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static void Write(
        string path,
        IReadOnlyList<ParsedEntry> entries,
        IReadOnlyList<CellQueryResult> results)
    {
        var byId = entries.ToDictionary(e => e.Id);

        using var writer = new StreamWriter(path);
        foreach (var result in results)
        {
            var entry = byId[result.QueryId];
            var record = new
            {
                query_id = result.QueryId,
                query = entry.Query,
                cell = ToObject(result.Cell),
                gold_sources = entry.GoldSources,
                metrics = result.Scores is { } s
                    ? new
                    {
                        recall_at_5 = s.RecallAt5,
                        recall_at_10 = s.RecallAt10,
                        recall_at_20 = s.RecallAt20,
                        mrr_at_10 = s.MrrAt10,
                    }
                    : null,
                latency_ms = result.LatencyMs,
                error = result.Error,
                hits = result.Hits?.Select(h => new
                {
                    rank = h.Rank,
                    chunk_id = h.ChunkId,
                    source_id = h.SourceId,
                    score = h.Score,
                    gold_hit = h.GoldHit,
                }),
            };

            writer.WriteLine(JsonSerializer.Serialize(record, Options));
        }
    }

    private static Dictionary<string, object> ToObject(Cell cell)
    {
        var obj = new Dictionary<string, object>();
        foreach (var (knob, value) in cell.Values)
            obj[knob] = value;
        return obj;
    }
}
