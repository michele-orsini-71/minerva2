using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Search;
using NSubstitute;

namespace Minerva.Tests.Search;

[Trait("Category", "Search")]
public class SearchPipelineTests
{
    private static readonly float[] QueryVector = [0.1f, 0.2f, 0.3f];

    private static SearchOptions DefaultOptions => new()
    {
        TopK = 10,
        HybridAlpha = 0.5,
        ExpandContext = false,
        CandidatePoolSize = 50,
        EnableReranker = true,
    };

    // The pipeline requires an IReranker; these tests don't exercise reranking,
    // so this passes candidates through unchanged.
    private sealed class NoopReranker : IReranker
    {
        public Task<IReadOnlyList<ScoredChunk>> Rank(
            string query, IReadOnlyList<ScoredChunk> candidates, CancellationToken cancellationToken) =>
            Task.FromResult(candidates);
    }

    // Reverses candidate order, so a test can observe whether reranking actually ran.
    private sealed class ReversingReranker : IReranker
    {
        public Task<IReadOnlyList<ScoredChunk>> Rank(
            string query, IReadOnlyList<ScoredChunk> candidates, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScoredChunk>>(candidates.Reverse().ToList());
    }

    private static IEmbeddingService MockEmbedder()
    {
        var embedder = Substitute.For<IEmbeddingService>();
        embedder.EmbedAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((IReadOnlyList<float[]>)new[] { QueryVector }));
        return embedder;
    }

    private static ChunkSearchRecord MakeRecord(
        string id, string collection = "c", string? prev = null, string? next = null) =>
        new(Id: id, SourceId: $"src-{id}", CollectionName: collection,
            ChunkIndex: 0, Content: $"content-{id}", Distance: 1.0,
            PrevChunkId: prev, NextChunkId: next);

    [Fact]
    public async Task SearchAsync_RunsVectorAndFtsAndReturnsFusedResults()
    {
        var repo = Substitute.For<IChunkQuery>();
        repo.VectorSearchAsync("c", Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MakeRecord("a"), MakeRecord("b") });
        repo.FullTextSearchAsync("c", "hello", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MakeRecord("b"), MakeRecord("d") });

        var pipeline = new SearchPipeline(
            MockEmbedder(),
            new VectorSearch(repo),
            new FullTextSearch(repo),
            new ContextExpander(repo),
            new NoopReranker(),
            NullLogger<SearchPipeline>.Instance);

        var results = await pipeline.SearchAsync(
            "hello", "c", DefaultOptions);

        await repo.Received(1).VectorSearchAsync(
            "c", Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await repo.Received(1).FullTextSearchAsync(
            "c", "hello", Arg.Any<int>(), Arg.Any<CancellationToken>());

        Assert.Equal(3, results.Count);
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i - 1].Score >= results[i].Score);

        Assert.Equal("b", results[0].ChunkId);
    }

    [Fact]
    public async Task SearchAsync_SkipsContextExpansionWhenDisabled()
    {
        var repo = Substitute.For<IChunkQuery>();
        repo.VectorSearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MakeRecord("a", prev: "p", next: "n") });
        repo.FullTextSearchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ChunkSearchRecord>());

        var pipeline = new SearchPipeline(
            MockEmbedder(),
            new VectorSearch(repo),
            new FullTextSearch(repo),
            new ContextExpander(repo),
            new NoopReranker(),
            NullLogger<SearchPipeline>.Instance);

        var results = await pipeline.SearchAsync(
            "q", "c", DefaultOptions with { ExpandContext = false });

        Assert.Single(results);
        Assert.Null(results[0].ContextBefore);
        Assert.Null(results[0].ContextAfter);

        await repo.DidNotReceive().GetAdjacentChunksAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_ExpandsContextWhenEnabled()
    {
        var repo = Substitute.For<IChunkQuery>();
        repo.VectorSearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MakeRecord("a", prev: "p", next: "n") });
        repo.FullTextSearchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ChunkSearchRecord>());
        repo.GetAdjacentChunksAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new ChunkRecord("p", "src-a", "c", 0, "prev-content", "h"),
                new ChunkRecord("n", "src-a", "c", 2, "next-content", "h"),
            });

        var pipeline = new SearchPipeline(
            MockEmbedder(),
            new VectorSearch(repo),
            new FullTextSearch(repo),
            new ContextExpander(repo),
            new NoopReranker(),
            NullLogger<SearchPipeline>.Instance);

        var results = await pipeline.SearchAsync(
            "q", "c", DefaultOptions with { ExpandContext = true });

        Assert.Single(results);
        Assert.Equal("prev-content", results[0].ContextBefore);
        Assert.Equal("next-content", results[0].ContextAfter);
    }

    [Fact]
    public async Task SearchAsync_RespectsTopK()
    {
        var repo = Substitute.For<IChunkQuery>();
        repo.VectorSearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 20).Select(i => MakeRecord($"v{i}")).ToArray());
        repo.FullTextSearchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ChunkSearchRecord>());

        var pipeline = new SearchPipeline(
            MockEmbedder(),
            new VectorSearch(repo),
            new FullTextSearch(repo),
            new ContextExpander(repo),
            new NoopReranker(),
            NullLogger<SearchPipeline>.Instance);

        var results = await pipeline.SearchAsync(
            "q", "c", DefaultOptions with { TopK = 5 });

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task SearchAsync_TogglesReranker_ViaEnableRerankerOption()
    {
        var repo = Substitute.For<IChunkQuery>();
        repo.VectorSearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MakeRecord("x1"), MakeRecord("x2"), MakeRecord("x3") });
        repo.FullTextSearchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ChunkSearchRecord>());

        var pipeline = new SearchPipeline(
            MockEmbedder(),
            new VectorSearch(repo),
            new FullTextSearch(repo),
            new ContextExpander(repo),
            new ReversingReranker(),
            NullLogger<SearchPipeline>.Instance);

        var disabled = await pipeline.SearchAsync("q", "c", DefaultOptions with { EnableReranker = false });
        var enabled = await pipeline.SearchAsync("q", "c", DefaultOptions with { EnableReranker = true });

        // Disabled: the reranker is skipped, fused (vector) order preserved.
        Assert.Equal(["x1", "x2", "x3"], disabled.Select(r => r.ChunkId).ToArray());
        // Enabled: the reranker runs and reverses the order.
        Assert.Equal(["x3", "x2", "x1"], enabled.Select(r => r.ChunkId).ToArray());
    }
}
