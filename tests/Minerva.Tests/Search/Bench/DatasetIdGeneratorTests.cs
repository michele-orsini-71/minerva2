using Minerva.Search.Bench.Authoring;

namespace Minerva.Tests.Search.Bench;

[Trait("Category", "Bench")]
public class DatasetIdGeneratorTests
{
    [Fact]
    public void EmptySet_StartsAt1()
    {
        var id = DatasetIdGenerator.GenerateId("src/a.md", new HashSet<string>());
        Assert.Equal("src/a.md-1", id);
    }

    [Fact]
    public void NoMatchingIdsForSource_StartsAt1()
    {
        var existing = new HashSet<string> { "src/b.md-1", "src/c.md-7" };
        var id = DatasetIdGenerator.GenerateId("src/a.md", existing);
        Assert.Equal("src/a.md-1", id);
    }

    [Fact]
    public void SingleExistingId_ReturnsNext()
    {
        var existing = new HashSet<string> { "src/a.md-1" };
        var id = DatasetIdGenerator.GenerateId("src/a.md", existing);
        Assert.Equal("src/a.md-2", id);
    }

    [Fact]
    public void ContiguousIds_ReturnsMaxPlus1()
    {
        var existing = new HashSet<string> { "src/a.md-1", "src/a.md-2", "src/a.md-3" };
        var id = DatasetIdGenerator.GenerateId("src/a.md", existing);
        Assert.Equal("src/a.md-4", id);
    }

    [Fact]
    public void GapsInSequence_UsesMaxNotCount()
    {
        // hand-edited file may have removed entries → gap from -2 to -5
        var existing = new HashSet<string> { "src/a.md-1", "src/a.md-2", "src/a.md-5" };
        var id = DatasetIdGenerator.GenerateId("src/a.md", existing);
        Assert.Equal("src/a.md-6", id);
    }

    [Fact]
    public void OtherSourceIds_IgnoredForCounter()
    {
        var existing = new HashSet<string>
        {
            "src/a.md-1",
            "src/b.md-9",
            "src/c.md-42",
        };
        var id = DatasetIdGenerator.GenerateId("src/a.md", existing);
        Assert.Equal("src/a.md-2", id);
    }

    [Fact]
    public void NonMatchingIdsForSameSource_IgnoredForCounter()
    {
        // hand-edited entries that don't follow the <source>-<n> pattern
        // must not influence the counter, but still occupy the seen-ids set
        var existing = new HashSet<string>
        {
            "src/a.md-1",
            "src/a.md-custom",
            "src/a.md-2-extra",
        };
        var id = DatasetIdGenerator.GenerateId("src/a.md", existing);
        Assert.Equal("src/a.md-2", id);
    }

    [Fact]
    public void SourceIdThatIsPrefixOfAnother_NotConfused()
    {
        // sourceId "foo" must not pick up ids belonging to "foobar"
        var existing = new HashSet<string> { "foobar-5", "foo-baz-3" };
        var id = DatasetIdGenerator.GenerateId("foo", existing);
        Assert.Equal("foo-1", id);
    }

    [Fact]
    public void SourceIdWithRegexSpecialChars_HandledLiterally()
    {
        // dots, slashes, plus signs in a source id must not be treated as regex
        var existing = new HashSet<string> { "a.b/c+d-1", "a.b/c+d-2" };
        var id = DatasetIdGenerator.GenerateId("a.b/c+d", existing);
        Assert.Equal("a.b/c+d-3", id);
    }

    [Fact]
    public void SuffixMustBeAllDigits()
    {
        // "foo-1a" is not a counter-shaped id; counter should still be 1
        var existing = new HashSet<string> { "foo-1a" };
        var id = DatasetIdGenerator.GenerateId("foo", existing);
        Assert.Equal("foo-1", id);
    }

    [Fact]
    public void GeneratedIdNotInExistingSet()
    {
        // round-trip: generated id must always be free
        var existing = new HashSet<string> { "src/a.md-1", "src/a.md-2", "src/a.md-3" };
        var id = DatasetIdGenerator.GenerateId("src/a.md", existing);
        Assert.DoesNotContain(id, existing);
    }
}
