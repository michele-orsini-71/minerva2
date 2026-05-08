using System.Text;
using Minerva.Ingestion;
using Minerva.Models;

namespace Minerva.Tests.Ingestion;

[Trait("Category", "Ingestion")]
public class SplitMarkdownToBudgetTests
{
    private static DocumentChunker CreateChunker(
        int targetChunkSize = 1200, int overlap = 200, int largeDocThreshold = 8000)
    {
        return new DocumentChunker(new ChunkingOptions
        {
            TargetChunkSize = targetChunkSize,
            ChunkOverlap = overlap,
            LargeDocumentThreshold = largeDocThreshold,
        });
    }

    // -----------------------------------------------------------------------
    // Contract: every output is ≤ budget. Regression tests for Phase A.
    // -----------------------------------------------------------------------

    [Fact]
    public void SegmentDocument_SingleTopLevelHeading_RespectsBudget()
    {
        // MacTree-shaped synthetic doc: one '#' at the top, many lower-level
        // headings beneath it, total far above threshold. The legacy segmenter
        // split only at the highest heading level and returned one giant segment;
        // the unified core must respect the budget regardless of heading shape.
        var sb = new StringBuilder();
        sb.AppendLine("# Top");
        sb.AppendLine();
        for (int i = 0; i < 100; i++)
        {
            sb.AppendLine($"## Section {i}");
            sb.AppendLine(new string('x', 1500));
            sb.AppendLine();
        }
        var text = sb.ToString();
        Assert.True(text.Length > 100_000,
            $"Test setup: doc must be large, was {text.Length}");

        const int budget = 8000;
        var chunker = CreateChunker(largeDocThreshold: budget);
        var segments = chunker.SegmentDocument(text);

        Assert.True(segments.Count > 1, "Large doc must split into multiple segments");
        Assert.All(segments, s =>
            Assert.True(s.Length <= budget,
                $"Segment of {s.Length} chars exceeds budget {budget}"));
    }

    [Fact]
    public void Chunk_NoSeparators_BruteForceFallbackRespectsBudget()
    {
        // 50k chars with no usable separator (e.g., a base64 blob). The recursive
        // splitter has no separator to split on; the brute-force fallback must
        // slice with overlap rather than emit a single oversized chunk.
        var text = new string('a', 50_000);

        const int maxChars = 5000;
        const int overlap = 200;
        var chunker = CreateChunker(targetChunkSize: maxChars, overlap: overlap);
        var chunks = chunker.Chunk("coll", "src1", text);

        Assert.All(chunks, c =>
            Assert.True(c.Content.Length <= maxChars,
                $"Chunk {c.ChunkIndex} of {c.Content.Length} chars exceeds budget {maxChars}"));

        // Brute-force step = maxChars - overlap = 4800; expected count ≈ 50000/4800 ≈ 11.
        // Assert a sane lower bound rather than an exact count to keep the test robust
        // against tail-absorption tweaks.
        Assert.True(chunks.Count >= 10,
            $"Expected at least 10 chunks for 50k/5k brute-force slicing, got {chunks.Count}");
    }

    // -----------------------------------------------------------------------
    // Behavior: header-aware splitting via the public Chunk / SegmentDocument
    // entry points. Moved from DocumentChunkerTests.
    // -----------------------------------------------------------------------

    [Fact]
    public void Chunk_SplitsMultiHeaderMarkdown()
    {
        var markdown = """
            # Section 1
            Content of section one.

            ## Section 1.1
            Sub-section content.

            # Section 2
            Content of section two.
            """;

        // Small TargetChunkSize prevents adjacent sections from being packed together,
        // so each heading boundary produces a separate chunk.
        var chunker = CreateChunker(targetChunkSize: 50, overlap: 10);
        var chunks = chunker.Chunk("coll", "src1", markdown);

        Assert.True(chunks.Count >= 3, $"Expected at least 3 chunks, got {chunks.Count}");
        Assert.Contains(chunks, c => c.Content.Contains("Section 1"));
        Assert.Contains(chunks, c => c.Content.Contains("Section 2"));
    }

    [Fact]
    public void Chunk_RespectsChunkSizeLimits()
    {
        var longContent = "# Title\n\n" + string.Join("\n\n", Enumerable.Range(0, 50)
            .Select(i => $"Paragraph {i}. " + new string('x', 100)));

        var chunker = CreateChunker(targetChunkSize: 500, overlap: 50);
        var chunks = chunker.Chunk("coll", "src1", longContent);

        Assert.True(chunks.Count > 1, "Long document should be split into multiple chunks");
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Content.Length <= 600,
                $"Chunk {chunk.ChunkIndex} is {chunk.Content.Length} chars, expected <= ~500");
        }
    }

    [Fact]
    public void Chunk_NoHeaders_FallsBackToSizeSplitting()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 30)
            .Select(i => $"Paragraph {i}. " + new string('x', 80)));

        var chunker = CreateChunker(targetChunkSize: 500, overlap: 50);
        var chunks = chunker.Chunk("coll", "src1", text);

        Assert.True(chunks.Count > 1, "Long text with no headers should still be split");
    }

    [Fact]
    public void Chunk_ShortDocument_SingleChunk()
    {
        var text = "# Title\nShort content.";
        var chunker = CreateChunker();

        var chunks = chunker.Chunk("coll", "src1", text);

        Assert.Single(chunks);
    }

    [Fact]
    public void SegmentDocument_SmallDocument_ReturnsSingleSegment()
    {
        var text = "# Title\nShort content.";
        var chunker = CreateChunker(largeDocThreshold: 8000);

        var segments = chunker.SegmentDocument(text);

        Assert.Single(segments);
        Assert.Equal(text, segments[0]);
    }

    [Fact]
    public void SegmentDocument_ExceedsBudget_AllSegmentsUnderBudget()
    {
        var sections = Enumerable.Range(1, 5)
            .Select(i => $"# Section {i}\n{new string('x', 2000)}")
            .ToList();
        var text = string.Join("\n\n", sections);

        const int budget = 3000;
        var chunker = CreateChunker(largeDocThreshold: budget);
        var segments = chunker.SegmentDocument(text);

        Assert.True(segments.Count > 1, "Large document should be split into segments");
        Assert.All(segments, s =>
            Assert.True(s.Length <= budget,
                $"Segment of {s.Length} chars exceeds budget {budget}"));
    }

    [Fact]
    public void Chunk_HeaderOnlySectionFollowedByOversized_NoOrphanedHeader()
    {
        var markdown = LoadFixture("orphan_header_before_oversized.md");
        var chunker = CreateChunker(targetChunkSize: 1200, overlap: 200);

        var chunks = chunker.Chunk("coll", "src1", markdown);

        Assert.All(chunks, c =>
            Assert.False(IsHeaderOnly(c.Content),
                $"Chunk {c.ChunkIndex} is header-only:\n{c.Content}"));
    }

    // -----------------------------------------------------------------------
    // Internal helpers exercised directly via internal visibility.
    // -----------------------------------------------------------------------

    [Fact]
    public void SplitByHeaders_ContentBeforeFirstHeader()
    {
        var text = "Preamble text\n\n# Header\nContent";

        var sections = DocumentChunker.SplitByHeaders(text);

        Assert.True(sections.Count >= 2);
        Assert.Equal("Preamble text", sections[0]);
    }

    [Fact]
    public void RecursiveSplit_TextUnderMax_ReturnsSingleElement()
    {
        var text = "Short text";
        var result = DocumentChunker.RecursiveSplit(text, 100, 20);

        Assert.Single(result);
        Assert.Equal("Short text", result[0]);
    }

    [Fact]
    public void RecursiveSplit_SplitsByParagraphBreaks()
    {
        var text = "Paragraph one.\n\nParagraph two.\n\nParagraph three.";
        var result = DocumentChunker.RecursiveSplit(text, 30, 0);

        Assert.True(result.Count >= 2);
        Assert.Contains(result, s => s.Contains("Paragraph one"));
    }

    private static string LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return File.ReadAllText(path);
    }

    private static bool IsHeaderOnly(string content)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (!trimmed.StartsWith('#')) return false;
        }
        return true;
    }
}
