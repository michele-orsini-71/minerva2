using System.Text.Json;
using Minerva.Models;
using Minerva.Search.Bench.Sweep;
using Minerva.Tests.TestSupport;

namespace Minerva.Tests.Search.Bench;


[Trait("Category", "Bench")]
public class RunJsonWriterTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 6, 5, 14, 30, 22, TimeSpan.Zero);

    private static readonly Collection TestCollection = new(
        "personal-notes-v1", null, TestOptions.Provenance(),
        new ClientProvenance("test-client", new Dictionary<string, object>()));

    private static SweepConfig Config(string? label = null) => new(
        "eval/datasets/private/personal-notes-v1.jsonl",
        "personal-notes-v1",
        50,
        100,
        label,
        new Dictionary<string, List<object>>
        {
            ["enable_reranker"] = [true, false],
            ["hybrid_alpha"] = [0.5],
        });

    private static JsonElement WriteAndParse(string? label = null)
    {
        var config = Config(label);
        var cells = CellEnumerator.Enumerate(config.Matrix);
        var path = Path.Combine(Path.GetTempPath(), $"run-{Guid.NewGuid():N}.json");
        try
        {
            RunJsonWriter.Write(path, Timestamp, "0.1.0+test", config, cells, TestCollection);
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
        Assert.Equal(50, root.GetProperty("top_k").GetInt32());
        Assert.Equal(100, root.GetProperty("candidate_pool_size").GetInt32());
    }

    [Fact]
    public void Label_Absent_WhenNull()
    {
        var root = WriteAndParse();

        Assert.False(root.TryGetProperty("label", out _));
    }

    [Fact]
    public void Label_Written_WhenSet()
    {
        var root = WriteAndParse("reranker");

        Assert.Equal("reranker", root.GetProperty("label").GetString());
    }

    [Fact]
    public void ResolvedSweep_EchoesFullConfig()
    {
        var root = WriteAndParse();
        var resolved = root.GetProperty("resolved_sweep");

        Assert.Equal("personal-notes-v1", resolved.GetProperty("collection").GetString());
        Assert.Equal(50, resolved.GetProperty("top_k").GetInt32());
        Assert.Equal(100, resolved.GetProperty("candidate_pool_size").GetInt32());
        var enableReranker = resolved.GetProperty("matrix").GetProperty("enable_reranker");
        Assert.Equal(2, enableReranker.GetArrayLength());
        Assert.True(enableReranker[0].GetBoolean());
    }

    [Fact]
    public void PlusSign_IsWrittenLiterally_NotEscaped()
    {
        var config = Config();
        var cells = CellEnumerator.Enumerate(config.Matrix);
        var path = Path.Combine(Path.GetTempPath(), $"run-{Guid.NewGuid():N}.json");
        try
        {
            RunJsonWriter.Write(path, Timestamp, "0.1.0+abc1234", config, cells, TestCollection);
            var raw = File.ReadAllText(path);

            Assert.Contains("0.1.0+abc1234", raw);
            Assert.DoesNotContain("\\u002B", raw);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cells_AreCartesianProduct_AsObjects()
    {
        var root = WriteAndParse();
        var cells = root.GetProperty("cells");

        Assert.Equal(2, cells.GetArrayLength()); // 2 x 1
        var first = cells[0];
        Assert.True(first.GetProperty("enable_reranker").GetBoolean());
        Assert.Equal(0.5, first.GetProperty("hybrid_alpha").GetDouble());
    }
}
