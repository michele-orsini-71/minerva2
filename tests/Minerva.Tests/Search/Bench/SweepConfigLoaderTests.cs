using Minerva.Search.Bench.Sweep;

namespace Minerva.Tests.Search.Bench;


[Trait("Category", "Bench")]
public class SweepConfigLoaderTests
{
    private const string GoodToml =
        """
        dataset = "eval/datasets/private/personal-notes-v1.jsonl"
        collection = "personal-notes-v1"
        top_k = 50
        candidate_pool_size = 100

        [matrix]
        enable_reranker = [true]
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
        Assert.Equal(50, result.Config.TopK);
        Assert.Equal(100, result.Config.CandidatePoolSize);
        Assert.Null(result.Config.Label);
        Assert.Equal(["enable_reranker", "hybrid_alpha"], result.Config.Matrix.Keys);
        Assert.Single(result.Config.Matrix["enable_reranker"]);
        Assert.Single(result.Config.Matrix["hybrid_alpha"]);
    }

    [Fact]
    public void Label_Present_IsPopulated()
    {
        var toml = "label = \"reranker\"\n" + GoodToml;

        var result = Load(toml);

        Assert.Empty(result.Errors);
        Assert.Equal("reranker", result.Config!.Label);
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
            top_k = 50
            candidate_pool_size = 100

            [matrix]
            hybrid_alpha = [0.5]
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
            top_k = 50
            candidate_pool_size = 100

            [matrix]
            hybrid_alpha = [0.5]
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
            top_k = 50
            candidate_pool_size = 100

            [matrix]
            hybrid_alpha = [0.5]
            """;

        var result = Load(toml);

        Assert.Contains(result.Errors, e => e.Contains("'collection'"));
    }

    // ---------- top_k / candidate_pool_size ----------

    [Fact]
    public void MissingTopK_ReportsError()
    {
        var toml =
            """
            dataset = "d"
            collection = "c"
            candidate_pool_size = 100

            [matrix]
            hybrid_alpha = [0.5]
            """;

        var result = Load(toml);

        Assert.Null(result.Config);
        Assert.Contains(result.Errors, e => e.Contains("'top_k'"));
    }

    [Fact]
    public void MissingCandidatePoolSize_ReportsError()
    {
        var toml =
            """
            dataset = "d"
            collection = "c"
            top_k = 50

            [matrix]
            hybrid_alpha = [0.5]
            """;

        var result = Load(toml);

        Assert.Null(result.Config);
        Assert.Contains(result.Errors, e => e.Contains("'candidate_pool_size'"));
    }

    // ---------- matrix ----------

    [Fact]
    public void MissingMatrix_ReportsError()
    {
        var toml =
            """
            dataset = "d"
            collection = "c"
            top_k = 50
            candidate_pool_size = 100
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
            top_k = 50
            candidate_pool_size = 100

            [matrix]
            hybrid_alpha = []
            """;

        var result = Load(toml);

        Assert.Contains(result.Errors, e => e.Contains("hybrid_alpha") && e.Contains("non-empty"));
    }

    [Fact]
    public void UnknownKnob_ReportsError()
    {
        var toml =
            """
            dataset = "d"
            collection = "c"
            top_k = 50
            candidate_pool_size = 100

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
        Assert.Contains(result.Errors, e => e.Contains("'top_k'"));
        Assert.Contains(result.Errors, e => e.Contains("'candidate_pool_size'"));
        Assert.Contains(result.Errors, e => e.Contains("[matrix]"));
        Assert.Equal(5, result.Errors.Count);
    }
}
