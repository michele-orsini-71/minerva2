using Minerva.Models;
using Minerva.Search;
using NSubstitute;

namespace Minerva.Tests.Search;

[Trait("Category", "Search")]
public class ContextExpanderTests
{
    private static ScoredChunk MakeResult(
        string id, string? prev = null, string? next = null, double score = 0.5) =>
        new(new ChunkSearchRecord(
            Id: id,
            SourceId: "src1",
            CollectionName: "c",
            ChunkIndex: 0,
            ContextualPrefix: null,
            Content: $"content-{id}",
            Distance: score,
            PrevChunkId: prev,
            NextChunkId: next), score);

    private static ChunkRecord MakeChunk(string id, string content) =>
        new(Id: id, SourceId: "src1", CollectionName: "c",
            ChunkIndex: 0, Content: content, ContentHash: "h");

    [Fact]
    public async Task ExpandAsync_FetchesPrevAndNextInSingleBatchCall()
    {
        var repo = Substitute.For<IChunkQuery>();
        repo.GetAdjacentChunksAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeChunk("p1", "before-1"),
                MakeChunk("n1", "after-1"),
                MakeChunk("p2", "before-2"),
                MakeChunk("n2", "after-2"),
            });

        var expander = new ContextExpander(repo);

        var results = new[]
        {
            MakeResult("m1", prev: "p1", next: "n1"),
            MakeResult("m2", prev: "p2", next: "n2"),
        };

        var expanded = await expander.ExpandAsync(results);

        // Exactly one batch call to GetAdjacentChunksAsync
        await repo.Received(1).GetAdjacentChunksAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());

        Assert.Equal(2, expanded.Count);
        Assert.Equal("before-1", expanded[0].ContextBefore);
        Assert.Equal("after-1", expanded[0].ContextAfter);
        Assert.Equal("before-2", expanded[1].ContextBefore);
        Assert.Equal("after-2", expanded[1].ContextAfter);
    }

    [Fact]
    public async Task ExpandAsync_HandlesEdgeChunksGracefully()
    {
        var repo = Substitute.For<IChunkQuery>();
        repo.GetAdjacentChunksAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MakeChunk("n1", "after-1") });

        var expander = new ContextExpander(repo);

        // First chunk has no prev, last has no next.
        var results = new[] { MakeResult("first", prev: null, next: "n1") };

        var expanded = await expander.ExpandAsync(results);

        Assert.Single(expanded);
        Assert.Null(expanded[0].ContextBefore);
        Assert.Equal("after-1", expanded[0].ContextAfter);
    }

    [Fact]
    public async Task ExpandAsync_EmptyResults_ReturnsEmpty()
    {
        var repo = Substitute.For<IChunkQuery>();
        var expander = new ContextExpander(repo);

        var expanded = await expander.ExpandAsync([]);

        Assert.Empty(expanded);
        // Should not hit the repository at all
        await repo.DidNotReceive().GetAdjacentChunksAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExpandAsync_NoAdjacentIds_DoesNotCallRepository()
    {
        var repo = Substitute.For<IChunkQuery>();
        var expander = new ContextExpander(repo);

        var results = new[] { MakeResult("only", prev: null, next: null) };

        var expanded = await expander.ExpandAsync(results);

        Assert.Single(expanded);
        Assert.Null(expanded[0].ContextBefore);
        Assert.Null(expanded[0].ContextAfter);

        await repo.DidNotReceive().GetAdjacentChunksAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExpandAsync_CarriesMetadataAndCollection()
    {
        var repo = Substitute.For<IChunkQuery>();
        repo.GetAdjacentChunksAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ChunkRecord>());

        var expander = new ContextExpander(repo);

        var metadata = new Dictionary<string, object> { ["key"] = "value" };
        var chunk = new ChunkSearchRecord(
            Id: "id1", SourceId: "src1", CollectionName: "coll-x",
            ChunkIndex: 3, ContextualPrefix: null, Content: "c", Distance: 0.7, Metadata: metadata);

        var expanded = await expander.ExpandAsync([new ScoredChunk(chunk, 0.9)]);

        Assert.Single(expanded);
        Assert.Equal("coll-x", expanded[0].CollectionName);
        Assert.Equal(0.9, expanded[0].Score);
        Assert.Same(metadata, expanded[0].Metadata);
    }
}
