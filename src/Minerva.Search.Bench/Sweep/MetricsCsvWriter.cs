using System.Globalization;
using CsvHelper;

namespace Minerva.Search.Bench.Sweep;

public static class MetricsCsvWriter
{
    public static void Write(
        string path,
        IReadOnlyList<string> knobOrder,
        IReadOnlyList<CellQueryResult> results)
    {
        using var writer = new StreamWriter(path);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteField("query_id");
        foreach (var knob in knobOrder)
            csv.WriteField(knob);
        csv.WriteField("recall_at_5");
        csv.WriteField("recall_at_10");
        csv.WriteField("recall_at_20");
        csv.WriteField("success_at_5");
        csv.WriteField("success_at_10");
        csv.WriteField("success_at_20");
        csv.WriteField("mrr_at_10");
        csv.WriteField("latency_ms");
        csv.WriteField("error");
        csv.NextRecord();

        foreach (var result in results)
        {
            csv.WriteField(result.QueryId);

            var knobValues = result.Cell.Values.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var knob in knobOrder)
                csv.WriteField(knobValues.TryGetValue(knob, out var value)
                    ? Convert.ToString(value, CultureInfo.InvariantCulture)
                    : "");

            if (result.Scores is { } scores)
            {
                csv.WriteField(scores.RecallAt5);
                csv.WriteField(scores.RecallAt10);
                csv.WriteField(scores.RecallAt20);
                csv.WriteField(scores.SuccessAt5);
                csv.WriteField(scores.SuccessAt10);
                csv.WriteField(scores.SuccessAt20);
                csv.WriteField(scores.MrrAt10);
            }
            else
            {
                csv.WriteField("");
                csv.WriteField("");
                csv.WriteField("");
                csv.WriteField("");
                csv.WriteField("");
                csv.WriteField("");
                csv.WriteField("");
            }

            csv.WriteField(result.LatencyMs);
            csv.WriteField(result.Error ?? "");
            csv.NextRecord();
        }
    }
}
