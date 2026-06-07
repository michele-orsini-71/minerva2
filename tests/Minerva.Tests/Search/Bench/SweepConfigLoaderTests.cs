using Minerva.Search.Bench.Sweep;

namespace Minerva.Tests.Search.Bench;


[Trait("Category", "Bench")]
public class SweepConfigLoaderTests
{
    private const string GoodToml =
        """
        dataset = "eval/datasets/private/personal-notes-v1.jsonl"
        collection = "personal-notes-v1"

        [matrix]
        top_k = [10]
        hybrid_alpha = [0.5]
        """;

    private static SweepLoadResult Load(string toml) => SweepConfigLoader.Load(toml);

    // ---------- happy path ----------

    [Fact]
    public void ValidToml_NoErrors_ConfigPopulated()
    {
        var result = Load(GoodToml);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Config);
        Assert.Equal("eval/datasets/private/personal-notes-v1.jsonl", result.Config!.Dataset);
        Assert.Equal("personal-notes-v1", result.Config.Collection);
        Assert.Equal(["top_k", "hybrid_alpha"], result.Config.Matrix.Keys);
        Assert.Single(result.Config.Matrix["top_k"]);
        Assert.Single(result.Config.Matrix["hybrid_alpha"]);
    }

    // ---------- parse failure ----------

    [Fact]
    public void MalformedToml_ReportsParseError_NullConfig()
    {
        var result = Load("[matrix");

        Assert.Null(result.Config);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Malformed TOML", error);
    }

    // ---------- dataset / collection ----------

    [Fact]
    public void MissingDataset_ReportsError()
    {
        var toml =
            """
            collection = "c"

            [matrix]
            top_k = [10]
            """;

        var result = Load(toml);

        Assert.Null(result.Config);
        Assert.Contains(result.Errors, e => e.Contains("'dataset'"));
    }

    [Fact]
    public void WhitespaceDataset_ReportsError()
    {
        var toml =
            """
            dataset = "   "
            collection = "c"

            [matrix]
            top_k = [10]
            """;

        var result = Load(toml);

        Assert.Contains(result.Errors, e => e.Contains("'dataset'"));
    }

    [Fact]
    public void MissingCollection_ReportsError()
    {
        var toml =
            """
            dataset = "d"

            [matrix]
            top_k = [10]
            """;

        var result = Load(toml);

        Assert.Contains(result.Errors, e => e.Contains("'collection'"));
    }

    // ---------- matrix ----------

    [Fact]
    public void MissingMatrix_ReportsError()
    {
        var toml =
            """
            dataset = "d"
            collection = "c"
            """;

        var result = Load(toml);

        Assert.Contains(result.Errors, e => e.Contains("[matrix]"));
    }

    [Fact]
    public void EmptyKnobList_ReportsError()
    {
        var toml =
            """
            dataset = "d"
            collection = "c"

            [matrix]
            top_k = []
            """;

        var result = Load(toml);

        Assert.Contains(result.Errors, e => e.Contains("top_k") && e.Contains("non-empty"));
    }

    [Fact]
    public void UnknownKnob_ReportsError()
    {
        var toml =
            """
            dataset = "d"
            collection = "c"

            [matrix]
            bogus = [1]
            """;

        var result = Load(toml);

        Assert.Contains(result.Errors, e => e.Contains("unknown matrix knob 'bogus'"));
    }

    // ---------- collect-all ----------

    [Fact]
    public void MultipleProblems_AllReported()
    {
        var toml =
            """
            dataset = ""
            collection = ""
            """;

        var result = Load(toml);

        Assert.Null(result.Config);
        Assert.Contains(result.Errors, e => e.Contains("'dataset'"));
        Assert.Contains(result.Errors, e => e.Contains("'collection'"));
        Assert.Contains(result.Errors, e => e.Contains("[matrix]"));
        Assert.Equal(3, result.Errors.Count);
    }
}
