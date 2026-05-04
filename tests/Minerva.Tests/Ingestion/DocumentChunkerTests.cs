using Minerva.Exceptions;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Utilities;

namespace Minerva.Tests.Ingestion;

[Trait("Category", "Ingestion")]
public class DocumentChunkerTests
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

        // Small TargetChunkSize keeps each section above the tail-absorption threshold (Target/4),
        // so heading-based splits aren't folded back together.
        var chunker = CreateChunker(targetChunkSize: 80, overlap: 10);
        var chunks = chunker.Chunk("coll", "src1", markdown);

        Assert.True(chunks.Count >= 3, $"Expected at least 3 chunks, got {chunks.Count}");
        Assert.Contains(chunks, c => c.Content.Contains("Section 1"));
        Assert.Contains(chunks, c => c.Content.Contains("Section 2"));
    }

    [Fact]
    public void Chunk_RespectsChunkSizeLimits()
    {
        // Build a document with one long section
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
    public void Chunk_GeneratesDeterministicIds()
    {
        var text = "# Header\nSome content here.";
        var chunker = CreateChunker();

        var chunks1 = chunker.Chunk("coll", "src1", text);
        var chunks2 = chunker.Chunk("coll", "src1", text);

        Assert.Equal(chunks1.Count, chunks2.Count);
        for (int i = 0; i < chunks1.Count; i++)
        {
            Assert.Equal(chunks1[i].Id, chunks2[i].Id);
            Assert.Equal(HashHelper.GenerateChunkId("src1", i), chunks1[i].Id);
        }
    }

    [Fact]
    public void Chunk_DifferentSourceIds_ProduceDifferentChunkIds()
    {
        var text = "# Header\nSome content here.";
        var chunker = CreateChunker();

        var chunks1 = chunker.Chunk("coll", "src1", text);
        var chunks2 = chunker.Chunk("coll", "src2", text);

        Assert.NotEqual(chunks1[0].Id, chunks2[0].Id);
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
    public void Chunk_ThrowsOnEmptyText()
    {
        var chunker = CreateChunker();

        Assert.Throws<ChunkingException>(() => chunker.Chunk("coll", "src1", ""));
        Assert.Throws<ChunkingException>(() => chunker.Chunk("coll", "src1", "   "));
    }

    [Fact]
    public void Chunk_ShortDocument_SingleChunk()
    {
        var text = "# Title\nShort content.";
        var chunker = CreateChunker();

        var chunks = chunker.Chunk("coll", "src1", text);

        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal("src1", chunks[0].SourceId);
        Assert.Equal("coll", chunks[0].CollectionName);
    }

    [Fact]
    public void Chunk_IndicesAreSequential()
    {
        var text = "# A\nContent A\n\n# B\nContent B\n\n# C\nContent C";
        var chunker = CreateChunker();

        var chunks = chunker.Chunk("coll", "src1", text);

        for (int i = 0; i < chunks.Count; i++)
            Assert.Equal(i, chunks[i].ChunkIndex);
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
    public void SegmentDocument_LargeDocument_SplitsAtTopLevelHeadings()
    {
        // Build a document that exceeds the threshold
        var sections = Enumerable.Range(1, 5)
            .Select(i => $"# Section {i}\n{new string('x', 2000)}")
            .ToList();
        var text = string.Join("\n\n", sections);

        var chunker = CreateChunker(largeDocThreshold: 3000);
        var segments = chunker.SegmentDocument(text);

        Assert.True(segments.Count > 1, "Large document should be split into segments");
    }

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
}
