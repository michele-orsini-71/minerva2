using System.Text.Json;
using Minerva.Search.Bench.Common;
using Minerva.Search.Bench.Metrics;
using Minerva.Search.Bench.Sweep;

namespace Minerva.Tests.Search.Bench;


[Trait("Category", "Bench")]
public class DetailsJsonlWriterTests
{
    private static Cell Cell(int topK)
        => new([new KeyValuePair<string, object>("top_k", (long)topK)]);

    private static string[] WriteAndReadLines(
        IReadOnlyList<ParsedEntry> entries, IReadOnlyList<CellQueryResult> results)
    {
        var path = Path.Combine(Path.GetTempPath(), $"details-{Guid.NewGuid():N}.jsonl");
        try
        {
            DetailsJsonlWriter.Write(path, entries, results);
            return File.ReadAllLines(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OneLinePerResult_SuccessRecordShape()
    {
        var entries = new[] { new ParsedEntry(1, "q1", "vulnerabilita", ["g"]) };
        var results = new[]
        {
            new CellQueryResult(
                "q1", Cell(10), new MetricScores(1.0, 1.0, 1.0, 1.0), 5, null,
                [new RetrievedHit(1, "c1", "g", 0.8, true), new RetrievedHit(2, "c2", "x", 0.4, false)]),
        };

        var lines = WriteAndReadLines(entries, results);

        var line = Assert.Single(lines);
        var root = JsonDocument.Parse(line).RootElement;

        Assert.Equal("q1", root.GetProperty("query_id").GetString());
        Assert.Equal("vulnerabilita", root.GetProperty("query").GetString());
        Assert.Equal(10, root.GetProperty("cell").GetProperty("top_k").GetInt32());
        Assert.Equal("g", root.GetProperty("gold_sources")[0].GetString());
        Assert.Equal(1.0, root.GetProperty("metrics").GetProperty("recall_at_5").GetDouble());

        var hits = root.GetProperty("hits");
        Assert.Equal(2, hits.GetArrayLength());
        Assert.Equal("g", hits[0].GetProperty("source_id").GetString());
        Assert.True(hits[0].GetProperty("gold_hit").GetBoolean());
        Assert.False(hits[1].GetProperty("gold_hit").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
    }

    [Fact]
    public void ErrorRecord_HasNullMetricsAndHits()
    {
        var entries = new[] { new ParsedEntry(1, "q1", "boom", ["b"]) };
        var results = new[]
        {
            new CellQueryResult("q1", Cell(10), null, 0, "search failed", null),
        };

        var lines = WriteAndReadLines(entries, results);

        var root = JsonDocument.Parse(lines[0]).RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("metrics").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("hits").ValueKind);
        Assert.Equal("search failed", root.GetProperty("error").GetString());
    }

    [Fact]
    public void NonAscii_IsWrittenLiterally_NotEscaped()
    {
        var entries = new[] { new ParsedEntry(1, "q1", "vulnerabilità", ["g"]) };
        var results = new[]
        {
            new CellQueryResult("q1", Cell(10), new MetricScores(1, 1, 1, 1), 1, null, []),
        };

        var lines = WriteAndReadLines(entries, results);

        Assert.Contains("vulnerabilità", lines[0]);
        Assert.DoesNotContain("\\u00E0", lines[0]);
    }

    [Fact]
    public void LineCount_EqualsResultCount()
    {
        var entries = new[]
        {
            new ParsedEntry(1, "q1", "a", ["g1"]),
            new ParsedEntry(2, "q2", "b", ["g2"]),
        };
        var results = new[]
        {
            new CellQueryResult("q1", Cell(10), new MetricScores(1, 1, 1, 1), 1, null, []),
            new CellQueryResult("q2", Cell(10), new MetricScores(0, 0, 0, 0), 1, null, []),
            new CellQueryResult("q1", Cell(20), new MetricScores(1, 1, 1, 1), 1, null, []),
            new CellQueryResult("q2", Cell(20), new MetricScores(0, 0, 0, 0), 1, null, []),
        };

        var lines = WriteAndReadLines(entries, results);

        Assert.Equal(4, lines.Length);
    }
}
