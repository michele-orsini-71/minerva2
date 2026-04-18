using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Models;
using Minerva.Search;
using Minerva.Storage;
using NSubstitute;

namespace Minerva.Tests.Search;

[Trait("Category", "Search")]
public class SearchPipelineTests
{
    private static readonly float[] QueryVector = [0.1f, 0.2f, 0.3f];

    private static IEmbeddingGenerator<string, Embedding<float>> MockEmbedder()
    {
        var embedder = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        embedder.GenerateAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<EmbeddingGenerationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var result = new GeneratedEmbeddings<Embedding<float>>();
                result.Add(new Embedding<float>(QueryVector));
                return Task.FromResult(result);
            });
        return embedder;
    }

    private static ChunkSearchRecord MakeRecord(
        string id, string collection = "c", string? prev = null, string? next = null) =>
        new(Id: id, SourceId: $"src-{id}", CollectionName: collection,
            ChunkIndex: 0, Content: $"content-{id}", Score: 1.0,
            PrevChunkId: prev, NextChunkId: next);

    [Fact]
    public async Task SearchAsync_RunsVectorAndFtsAndReturnsFusedResults()
    {
        var repo = Substitute.For<IChunkRepository>();
        repo.VectorSearchAsync("c", Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MakeRecord("a"), MakeRecord("b") });
        repo.FullTextSearchAsync("c", "hello", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MakeRecord("b"), MakeRecord("d") });

        var pipeline = new SearchPipeline(
            MockEmbedder(),
            new VectorSearch(repo),
            new FullTextSearch(repo),
            new ContextExpander(repo),
            NullLogger<SearchPipeline>.Instance);

        var results = await pipeline.SearchAsync(
            "hello", ["c"], new SearchOptions(TopK: 10));

        // Both search paths should have been hit
        await repo.Received(1).VectorSearchAsync(
            "c", Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await repo.Received(1).FullTextSearchAsync(
            "c", "hello", Arg.Any<int>(), Arg.Any<CancellationToken>());

        // Results are fused (union of both lists) and ordered by score descending
        Assert.Equal(3, results.Count);
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i - 1].Score >= results[i].Score);

        // "b" appears in both lists and should rank first
        Assert.Equal("b", results[0].ChunkId);
    }

    [Fact]
    public async Task SearchAsync_SkipsContextExpansionWhenDisabled()
    {
        var repo = Substitute.For<IChunkRepository>();
        repo.VectorSearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MakeRecord("a", prev: "p", next: "n") });
        repo.FullTextSearchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ChunkSearchRecord>());

        var pipeline = new SearchPipeline(
            MockEmbedder(),
            new VectorSearch(repo),
            new FullTextSearch(repo),
            new ContextExpander(repo),
            NullLogger<SearchPipeline>.Instance);

        var results = await pipeline.SearchAsync(
            "q", ["c"], new SearchOptions(ExpandContext: false));

        Assert.Single(results);
        Assert.Null(results[0].ContextBefore);
        Assert.Null(results[0].ContextAfter);

        await repo.DidNotReceive().GetAdjacentChunksAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_ExpandsContextWhenEnabled()
    {
        var repo = Substitute.For<IChunkRepository>();
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
            NullLogger<SearchPipeline>.Instance);

        var results = await pipeline.SearchAsync(
            "q", ["c"], new SearchOptions(ExpandContext: true));

        Assert.Single(results);
        Assert.Equal("prev-content", results[0].ContextBefore);
        Assert.Equal("next-content", results[0].ContextAfter);
    }

    [Fact]
    public async Task SearchAsync_MultiCollection_MergesAcrossCollections()
    {
        var repo = Substitute.For<IChunkRepository>();
        repo.VectorSearchAsync("c1", Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MakeRecord("c1-a", collection: "c1") });
        repo.VectorSearchAsync("c2", Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MakeRecord("c2-a", collection: "c2") });
        repo.FullTextSearchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ChunkSearchRecord>());

        var pipeline = new SearchPipeline(
            MockEmbedder(),
            new VectorSearch(repo),
            new FullTextSearch(repo),
            new ContextExpander(repo),
            NullLogger<SearchPipeline>.Instance);

        var results = await pipeline.SearchAsync(
            "q", ["c1", "c2"], new SearchOptions(TopK: 10));

        Assert.Equal(2, results.Count);
        var collections = results.Select(r => r.CollectionName).ToHashSet();
        Assert.Contains("c1", collections);
        Assert.Contains("c2", collections);
    }

    [Fact]
    public async Task SearchAsync_EmptyCollections_ReturnsEmpty()
    {
        var repo = Substitute.For<IChunkRepository>();
        var pipeline = new SearchPipeline(
            MockEmbedder(),
            new VectorSearch(repo),
            new FullTextSearch(repo),
            new ContextExpander(repo),
            NullLogger<SearchPipeline>.Instance);

        var results = await pipeline.SearchAsync("q", [], new SearchOptions());

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_RespectsTopK()
    {
        var repo = Substitute.For<IChunkRepository>();
        repo.VectorSearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 20).Select(i => MakeRecord($"v{i}")).ToArray());
        repo.FullTextSearchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ChunkSearchRecord>());

        var pipeline = new SearchPipeline(
            MockEmbedder(),
            new VectorSearch(repo),
            new FullTextSearch(repo),
            new ContextExpander(repo),
            NullLogger<SearchPipeline>.Instance);

        var results = await pipeline.SearchAsync(
            "q", ["c"], new SearchOptions(TopK: 5));

        Assert.Equal(5, results.Count);
    }
}
