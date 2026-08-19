using Minerva.Search.Bench.Metrics;
using Minerva.Search.Bench.Sweep;

namespace Minerva.Tests.Search.Bench;


[Trait("Category", "Bench")]
public class MetricsCsvWriterTests
{
    private static Cell Cell(int topK)
        => new([new KeyValuePair<string, object>("top_k", (long)topK)]);

    private static string[] WriteAndReadLines(IReadOnlyList<CellQueryResult> results)
    {
        var path = Path.Combine(Path.GetTempPath(), $"metrics-{Guid.NewGuid():N}.csv");
        try
        {
            MetricsCsvWriter.Write(path, ["top_k"], results);
            return File.ReadAllLines(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Header_AndOneRowPerResult()
    {
        var results = new[]
        {
            new CellQueryResult("q1", Cell(10), new MetricScores(1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0), 5, null, null),
            new CellQueryResult("q2", Cell(10), new MetricScores(0.0, 0.5, 0.5, 0.0, 1.0, 1.0, 0.0), 7, null, null),
        };

        var lines = WriteAndReadLines(results);

        Assert.Equal(
            "query_id,top_k,recall_at_5,recall_at_10,recall_at_20,success_at_5,success_at_10,success_at_20,rr_at_10,latency_ms,error",
            lines[0]);
        Assert.Equal(3, lines.Length); // header + 2 rows
        Assert.Equal("q1,10,1,1,1,1,1,1,1,5,", lines[1]);
    }

    [Fact]
    public void ErrorRow_HasEmptyMetricCells_AndQuotedError()
    {
        var results = new[]
        {
            new CellQueryResult("q1", Cell(10), null, 0, "boom, failed", null),
        };

        var lines = WriteAndReadLines(results);

        // empty recall/rr cells, latency, then quoted error (contains a comma)
        Assert.Equal("q1,10,,,,,,,,0,\"boom, failed\"", lines[1]);
    }
}
