using System.Text.Json;
using Minerva.Search.Bench.Sweep;

namespace Minerva.Tests.Search.Bench;


[Trait("Category", "Bench")]
public class RunJsonWriterTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 6, 5, 14, 30, 22, TimeSpan.Zero);

    private static SweepConfig Config() => new()
    {
        Dataset = "eval/datasets/private/personal-notes-v1.jsonl",
        Collection = "personal-notes-v1",
        Matrix = new Dictionary<string, List<object>>
        {
            ["top_k"] = [10L, 20L],
            ["hybrid_alpha"] = [0.5],
        },
    };

    private static JsonElement WriteAndParse()
    {
        var config = Config();
        var cells = CellEnumerator.Enumerate(config.Matrix);
        var path = Path.Combine(Path.GetTempPath(), $"run-{Guid.NewGuid():N}.json");
        try
        {
            RunJsonWriter.Write(path, Timestamp, "0.1.0+test", config, cells);
            return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Tier1Fields_ArePresentAndCorrect()
    {
        var root = WriteAndParse();

        Assert.Equal("2026-06-05T14:30:22Z", root.GetProperty("timestamp").GetString());
        Assert.Equal("0.1.0+test", root.GetProperty("bench_version").GetString());
        Assert.Equal(
            "eval/datasets/private/personal-notes-v1.jsonl",
            root.GetProperty("dataset_path").GetString());
        Assert.Equal("personal-notes-v1", root.GetProperty("collection").GetString());
    }

    [Fact]
    public void ResolvedSweep_EchoesFullConfig()
    {
        var root = WriteAndParse();
        var resolved = root.GetProperty("resolved_sweep");

        Assert.Equal("personal-notes-v1", resolved.GetProperty("collection").GetString());
        var topK = resolved.GetProperty("matrix").GetProperty("top_k");
        Assert.Equal(2, topK.GetArrayLength());
        Assert.Equal(10, topK[0].GetInt32());
    }

    [Fact]
    public void Cells_AreCartesianProduct_AsObjects()
    {
        var root = WriteAndParse();
        var cells = root.GetProperty("cells");

        Assert.Equal(2, cells.GetArrayLength()); // 2 x 1
        var first = cells[0];
        Assert.Equal(10, first.GetProperty("top_k").GetInt32());
        Assert.Equal(0.5, first.GetProperty("hybrid_alpha").GetDouble());
    }
}
