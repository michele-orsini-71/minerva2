using Minerva.Search.Bench.Common;
using Minerva.Search.Bench.Validation;

namespace Minerva.Tests.Search.Bench;


[Trait("Category", "Bench")]
public class DatasetValidatorTests
{
    private const string GoodEntry =
        """{"id":"q001","query":"what is X","gold_sources":["src/a.md"]}""";

    private static PureValidationResult Run(string jsonl)
        => DatasetValidator.ValidatePure(new StringReader(jsonl));

    // ---------- happy path ----------

    [Fact]
    public void SingleValidEntry_NoIssues()
    {
        var result = Run(GoodEntry);

        Assert.Empty(result.Issues);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(1, entry.LineNumber);
        Assert.Equal("q001", entry.Id);
        Assert.Equal("what is X", entry.Query);
        Assert.Equal(["src/a.md"], entry.GoldSources);
    }

    [Fact]
    public void MultipleValidEntries_NoIssues()
    {
        var jsonl = string.Join("\n",
            """{"id":"q001","query":"what is X","gold_sources":["src/a.md","src/b.md"]}""",
            """{"id":"q002","query":"what is Y","gold_sources":["src/c.md"]}""");

        var result = Run(jsonl);

        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(["src/a.md", "src/b.md"], result.Entries[0].GoldSources);
    }

    [Fact]
    public void BlankAndWhitespaceLines_Skipped_DoNotShiftLineNumbers()
    {
        var jsonl = string.Join("\n",
            "",
            "   ",
            GoodEntry); // line 3

        var result = Run(jsonl);

        Assert.Empty(result.Issues);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(3, entry.LineNumber);
    }

    // ---------- JSON parsing ----------

    [Fact]
    public void MalformedJson_ProducesParseIssue_LineSkipped()
    {
        var result = Run("{not json");

        var issue = Assert.Single(result.Issues);
        Assert.Equal(1, issue.LineNumber);
        Assert.Null(issue.EntryId);
        Assert.Contains("malformed JSON", issue.Message);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void NonObjectRoot_ProducesIssue_LineSkipped()
    {
        var jsonl = string.Join("\n",
            "[1,2,3]",
            """ "just a string" """);

        var result = Run(jsonl);

        Assert.Equal(2, result.Issues.Count);
        Assert.All(result.Issues, i => Assert.Contains("expected a JSON object", i.Message));
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void MalformedLineDoesNotAbortFile_ValidLinesStillParsed()
    {
        var jsonl = string.Join("\n",
            "{garbage",
            GoodEntry);

        var result = Run(jsonl);

        Assert.Single(result.Issues);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(2, entry.LineNumber);
    }

    // ---------- id ----------

    [Fact]
    public void MissingId_Issue_EntryRejected()
    {
        var result = Run("""{"query":"q","gold_sources":["s"]}""");

        var issue = Assert.Single(result.Issues);
        Assert.Equal("missing required field: 'id'", issue.Message);
        Assert.Null(issue.EntryId);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void NonStringId_Issue_EntryRejected()
    {
        var result = Run("""{"id":42,"query":"q","gold_sources":["s"]}""");

        var issue = Assert.Single(result.Issues);
        Assert.Contains("'id' must be a string", issue.Message);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void EmptyId_Issue_EntryRejected()
    {
        var result = Run("""{"id":"","query":"q","gold_sources":["s"]}""");

        var issue = Assert.Single(result.Issues);
        Assert.Contains("non-empty, non-whitespace", issue.Message);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void WhitespaceId_Issue_EntryRejected()
    {
        var result = Run("""{"id":"   ","query":"q","gold_sources":["s"]}""");

        var issue = Assert.Single(result.Issues);
        Assert.Contains("non-empty, non-whitespace", issue.Message);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void DuplicateId_SecondOccurrenceFlagged_FirstSurvives()
    {
        var jsonl = string.Join("\n",
            GoodEntry,
            """{"id":"q001","query":"another","gold_sources":["src/b.md"]}""");

        var result = Run(jsonl);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(2, issue.LineNumber);
        Assert.Equal("q001", issue.EntryId);
        Assert.Contains("duplicate id", issue.Message);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(1, entry.LineNumber);
    }

    // ---------- query ----------

    [Fact]
    public void MissingQuery_Issue_EntryRejected()
    {
        var result = Run("""{"id":"q001","gold_sources":["s"]}""");

        var issue = Assert.Single(result.Issues);
        Assert.Equal("missing required field: 'query'", issue.Message);
        Assert.Equal("q001", issue.EntryId);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void NonStringQuery_Issue()
    {
        var result = Run("""{"id":"q001","query":42,"gold_sources":["s"]}""");

        var issue = Assert.Single(result.Issues);
        Assert.Contains("'query' must be a string", issue.Message);
    }

    [Fact]
    public void WhitespaceQuery_Issue()
    {
        var result = Run("""{"id":"q001","query":"   ","gold_sources":["s"]}""");

        var issue = Assert.Single(result.Issues);
        Assert.Contains("'query' must be a non-empty, non-whitespace string", issue.Message);
        Assert.Empty(result.Entries);
    }

    // ---------- gold_sources ----------

    [Fact]
    public void MissingGoldSources_Issue_EntryRejected()
    {
        var result = Run("""{"id":"q001","query":"q"}""");

        var issue = Assert.Single(result.Issues);
        Assert.Equal("missing required field: 'gold_sources'", issue.Message);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void NonArrayGoldSources_Issue()
    {
        var result = Run("""{"id":"q001","query":"q","gold_sources":"src/a.md"}""");

        var issue = Assert.Single(result.Issues);
        Assert.Contains("'gold_sources' must be an array", issue.Message);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void EmptyGoldSources_Issue()
    {
        var result = Run("""{"id":"q001","query":"q","gold_sources":[]}""");

        var issue = Assert.Single(result.Issues);
        Assert.Contains("at least one entry", issue.Message);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void NonStringGoldSourceEntry_Issue()
    {
        var result = Run("""{"id":"q001","query":"q","gold_sources":["ok", 42]}""");

        var issue = Assert.Single(result.Issues);
        Assert.Contains("'gold_sources' entries must be strings", issue.Message);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(["ok"], entry.GoldSources);
    }

    [Fact]
    public void EmptyGoldSourceEntry_Issue()
    {
        var result = Run("""{"id":"q001","query":"q","gold_sources":["ok", "  "]}""");

        var issue = Assert.Single(result.Issues);
        Assert.Contains("non-empty, non-whitespace", issue.Message);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(["ok"], entry.GoldSources);
    }

    [Fact]
    public void DuplicateSourceIdWithinEntry_Issue_DedupedInCollected()
    {
        var result = Run("""{"id":"q001","query":"q","gold_sources":["a","b","a"]}""");

        var issue = Assert.Single(result.Issues);
        Assert.Contains("duplicate source_id within 'gold_sources'", issue.Message);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(["a", "b"], entry.GoldSources);
    }

    // ---------- gold_sections (Phase 4 reservation) ----------

    [Fact]
    public void EmptyGoldSections_NoIssue()
    {
        var jsonl = """{"id":"q001","query":"q","gold_sources":["s"],"gold_sections":[]}""";

        var result = Run(jsonl);

        Assert.Empty(result.Issues);
        Assert.Single(result.Entries);
    }

    [Fact]
    public void PopulatedGoldSections_Issue()
    {
        var jsonl = """{"id":"q001","query":"q","gold_sources":["s"],"gold_sections":["x"]}""";

        var result = Run(jsonl);

        var issue = Assert.Single(result.Issues);
        Assert.Contains("reserved for Phase 4", issue.Message);
        // entry still parses for existence check; gold_sections doesn't block that
        Assert.Single(result.Entries);
    }

    // ---------- unknown keys ----------

    [Fact]
    public void UnknownTopLevelKey_Issue()
    {
        var jsonl = """{"id":"q001","query":"q","gold_sources":["s"],"foo":"bar"}""";

        var result = Run(jsonl);

        var issue = Assert.Single(result.Issues);
        Assert.Contains("unknown top-level key: 'foo'", issue.Message);
        Assert.Equal("q001", issue.EntryId);
    }

    [Fact]
    public void OptionalFieldsAccepted_NoIssue()
    {
        var jsonl = """
            {"id":"q001","query":"q","gold_sources":["s"],"answer_text":"...","notes":"regression case"}
            """;

        var result = Run(jsonl);

        Assert.Empty(result.Issues);
        Assert.Single(result.Entries);
    }

    // ---------- issue grouping ----------

    [Fact]
    public void IssuesOnSameEntry_AllCarryEntryId()
    {
        // missing query AND unknown key AND populated gold_sections, with valid id
        var jsonl = """
            {"id":"q001","gold_sources":["s"],"gold_sections":["x"],"foo":"bar"}
            """;

        var result = Run(jsonl);

        Assert.Equal(3, result.Issues.Count);
        Assert.All(result.Issues, i => Assert.Equal("q001", i.EntryId));
        Assert.All(result.Issues, i => Assert.Equal(1, i.LineNumber));
    }

    [Fact]
    public void CollectAll_MultipleIssuesAcrossLines()
    {
        var jsonl = string.Join("\n",
            """{"query":"no id","gold_sources":["s"]}""",          // line 1: missing id
            """{"id":"q002","gold_sources":["s"]}""",              // line 2: missing query
            GoodEntry.Replace("q001", "q003"));                    // line 3: ok

        var result = Run(jsonl);

        Assert.Equal(2, result.Issues.Count);
        Assert.Equal(1, result.Issues[0].LineNumber);
        Assert.Equal(2, result.Issues[1].LineNumber);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("q003", entry.Id);
    }
}
