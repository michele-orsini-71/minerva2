using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Utilities;

namespace Minerva.Tests.Ingestion;

[Trait("Category", "Ingestion")]
public class HeadingTrailAnnotatorTests
{
    private const string CollectionName = "test-collection";
    private const string SourceId = "doc.md";

    private static Chunk MakeChunk(int index, string content) => new(
        Id: HashHelper.GenerateChunkId(CollectionName, SourceId, index),
        SourceId: SourceId,
        CollectionName: CollectionName,
        ChunkIndex: index,
        Content: content,
        ContentHash: HashHelper.ComputeContentHash(content));

    private static List<Chunk> Annotate(params string[] contents) =>
        HeadingTrailAnnotator.Annotate(
            contents.Select((c, i) => MakeChunk(i, c)).ToList());

    [Fact]
    public void ChunkBeforeAnyHeading_GetsEmptyTrail()
    {
        var chunks = Annotate("Plain intro text without headings.");

        Assert.Equal([], chunks[0].HeadingTrail);
    }

    [Fact]
    public void ChunkOpeningWithHeading_IncludesThatHeading()
    {
        var chunks = Annotate("# Jamaica\n\nIntro text.");

        Assert.Equal(["Jamaica"], chunks[0].HeadingTrail);
    }

    [Fact]
    public void NestedHeadings_BuildFullTrail()
    {
        var chunks = Annotate(
            "# Jamaica\n\nIntro.\n\n## Government\n\n### Political parties",
            "Text about the PNP and JLP.");

        Assert.Equal(["Jamaica", "Government", "Political parties"], chunks[1].HeadingTrail);
    }

    [Fact]
    public void SiblingHeading_ReplacesPreviousAtSameLevel()
    {
        var chunks = Annotate(
            "# Jamaica\n\n## History\n\nOld times.",
            "## Geography\n\nMountains and coasts.");

        Assert.Equal(["Jamaica", "History"], chunks[0].HeadingTrail);
        Assert.Equal(["Jamaica", "Geography"], chunks[1].HeadingTrail);
    }

    [Fact]
    public void HeadingMidChunk_DoesNotAffectOwnTrail_ButAffectsNext()
    {
        var chunks = Annotate(
            "# Jamaica\n\n## History\n\nText.",
            "Tail of history.\n\n## Geography\n\nStart of geography.",
            "More geography.");

        Assert.Equal(["Jamaica", "History"], chunks[1].HeadingTrail);
        Assert.Equal(["Jamaica", "Geography"], chunks[2].HeadingTrail);
    }

    [Fact]
    public void LevelJumpBack_PopsDeeperHeadings()
    {
        var chunks = Annotate(
            "# Doc\n\n## A\n\n### A1\n\nDeep text.",
            "## B\n\nShallow again.");

        Assert.Equal(["Doc", "B"], chunks[1].HeadingTrail);
    }

    [Fact]
    public void RepeatedHeadingFromOverlap_IsIdempotent()
    {
        // Chunk overlap can replay a heading already seen at the end of the
        // previous chunk; the trail must not change because of it.
        var chunks = Annotate(
            "# Doc\n\n## Section\n\nFirst part.",
            "## Section\n\nSecond part (overlap repeats the heading).");

        Assert.Equal(["Doc", "Section"], chunks[1].HeadingTrail);
    }

    [Fact]
    public void SetextHeading_IsRecognized()
    {
        var chunks = Annotate("Jamaica\n=======\n\nIntro text.");

        Assert.Equal(["Jamaica"], chunks[0].HeadingTrail);
    }

    [Fact]
    public void RealChunkerOutput_CarriesTrailsAcrossChunks()
    {
        var options = new ChunkingOptions
        {
            TargetChunkSize = 120,
            ChunkOverlap = 20,
            MaxSegmentChars = 8000,
            ChunkerType = ChunkerType.Custom,
            Contextualization = new ContextualizationOptions { Level = ContextualizationLevel.None },
        };
        var chunker = new DocumentChunker(options);
        var text =
            "# Jamaica\n\n" +
            "Jamaica is an island country situated in the Caribbean Sea.\n\n" +
            "## Government\n\n" +
            "Jamaica is a parliamentary democracy and constitutional monarchy with " +
            "an elected House of Representatives and a nominated Senate.\n\n" +
            "## Geography\n\n" +
            "The island features the Blue Mountains inland and a narrow coastal plain.";

        var chunks = HeadingTrailAnnotator.Annotate(
            chunker.Chunk(CollectionName, SourceId, text));

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.NotNull(c.HeadingTrail));
        Assert.Contains(chunks, c => c.HeadingTrail!.SequenceEqual(["Jamaica", "Government"]));
        Assert.Contains(chunks, c => c.HeadingTrail!.SequenceEqual(["Jamaica", "Geography"]));
    }
}
