using Minerva.Exceptions;
using Minerva.Ingestion;
using Minerva.Tests.TestSupport;
using Minerva.Utilities;

namespace Minerva.Tests.Ingestion;

[Trait("Category", "Ingestion")]
public class DocumentChunkerTests
{
    private static DocumentChunker CreateChunker(
        int targetChunkSize = 1200, int overlap = 200)
    {
        return new DocumentChunker(TestOptions.Chunking(
            targetChunkSize: targetChunkSize,
            chunkOverlap: overlap));
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
            Assert.Equal(HashHelper.GenerateChunkId("coll", "src1", i), chunks1[i].Id);
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
    public void Chunk_ThrowsOnEmptyText()
    {
        var chunker = CreateChunker();

        Assert.Throws<ChunkingException>(() => chunker.Chunk("coll", "src1", ""));
        Assert.Throws<ChunkingException>(() => chunker.Chunk("coll", "src1", "   "));
    }

    [Fact]
    public void Chunk_PopulatesRecordFields()
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
}
