using Minerva.MarkdownIndexer;

namespace Minerva.Tests.Watcher;

[Trait("Category", "Watcher")]
public class MarkdownScannerTests : IDisposable
{
    private readonly string _root;

    public MarkdownScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "minerva-watcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private MarkdownScanner MakeScanner(string[]? excludes = null) =>
        new(new IndexerOptions
        {
            RootPath = _root,
            CollectionName = "test",
            ExcludeDirectories = excludes ?? [".obsidian", ".trash", ".git"],
        });

    private string WriteFile(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Fact]
    public void ScanFiles_ReturnsAllMarkdownFilesRecursively()
    {
        WriteFile("root.md", "a");
        WriteFile("notes/one.md", "b");
        WriteFile("notes/daily/two.md", "c");
        WriteFile("ignored.txt", "not markdown");

        var files = MakeScanner().ScanFiles().ToList();

        Assert.Equal(3, files.Count);
        Assert.All(files, f => Assert.EndsWith(".md", f));
    }

    [Fact]
    public void ScanFiles_ExcludesConfiguredDirectories()
    {
        WriteFile("note.md", "keep");
        WriteFile(".obsidian/workspace.md", "drop");
        WriteFile(".trash/old.md", "drop");
        WriteFile(".git/config.md", "drop");
        WriteFile("deep/.obsidian/nested.md", "drop");

        var files = MakeScanner().ScanFiles().ToList();

        Assert.Single(files);
        Assert.EndsWith("note.md", files[0]);
    }

    [Fact]
    public void ReadFile_ParsesFrontmatter_AndExtractsTitle()
    {
        var path = WriteFile("note.md",
            """
            ---
            title: My Note
            tags: [a, b]
            published: true
            ---
            Body content here.
            """);

        var doc = MakeScanner().ReadFile(path);

        Assert.Equal("My Note", doc.Title);
        Assert.NotNull(doc.Metadata);
        Assert.Equal("My Note", doc.Metadata!["title"]);
        Assert.True((bool)doc.Metadata["published"]);
        Assert.Contains("Body content here.", doc.Text);
        Assert.DoesNotContain("---", doc.Text);
    }

    [Fact]
    public void ReadFile_FallsBackToFilenameWhenFrontmatterHasNoTitle()
    {
        var path = WriteFile("my-note.md", "No frontmatter, plain body.");

        var doc = MakeScanner().ReadFile(path);

        Assert.Equal("my-note", doc.Title);
        Assert.Null(doc.Metadata);
        Assert.Equal("No frontmatter, plain body.", doc.Text);
    }

    [Fact]
    public void ReadFile_DerivesSourceIdFromRelativePath_WithForwardSlashes()
    {
        var path = WriteFile("notes/daily/2026-04-18.md", "x");

        var doc = MakeScanner().ReadFile(path);

        Assert.Equal("notes/daily/2026-04-18.md", doc.SourceId);
    }

    [Fact]
    public void ReadFile_ExtractsImagesAsAttachments()
    {
        var path = WriteFile("img.md",
            "Text before.\n![alt text](./images/diagram.png)\nMiddle.\n![](./no-alt.png)\nEnd.");

        var doc = MakeScanner().ReadFile(path);

        Assert.NotNull(doc.Attachments);
        Assert.Equal(2, doc.Attachments!.Count);

        var withAlt = doc.Attachments["![alt text](./images/diagram.png)"];
        Assert.Equal("alt text", withAlt.Description);
        Assert.Equal("./images/diagram.png", withAlt.SourcePath);

        var noAlt = doc.Attachments["![](./no-alt.png)"];
        Assert.Equal("no-alt", noAlt.Description);
    }

    [Fact]
    public void ReadFile_HandlesDocumentWithNoFrontmatter()
    {
        var path = WriteFile("plain.md", "Just text.\nSecond line.");

        var doc = MakeScanner().ReadFile(path);

        Assert.Equal("plain", doc.Title);
        Assert.Null(doc.Metadata);
        Assert.Equal("Just text.\nSecond line.", doc.Text);
    }

    [Fact]
    public void SplitFrontmatter_DoesNotTreatHorizontalRulesAsClosingFence()
    {
        const string raw =
            """
            ---
            title: X
            ---
            First paragraph.

            ---

            After horizontal rule.
            """;

        var (frontmatter, body) = MarkdownScanner.SplitFrontmatter(raw);

        Assert.NotNull(frontmatter);
        Assert.Contains("title: X", frontmatter!);
        Assert.StartsWith("First paragraph.", body);
        Assert.Contains("After horizontal rule.", body);
    }

    [Fact]
    public void SplitFrontmatter_ReturnsNullWhenDocumentDoesNotStartWithFence()
    {
        var (frontmatter, body) = MarkdownScanner.SplitFrontmatter("# Heading\n---\nContent");

        Assert.Null(frontmatter);
        Assert.Equal("# Heading\n---\nContent", body);
    }
}
