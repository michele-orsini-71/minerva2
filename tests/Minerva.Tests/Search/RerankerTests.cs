using Minerva.Models;
using Minerva.Search;

namespace Minerva.Tests.Search;

[Trait("Category", "Search")]
public class RerankerTests
{
    // Captures what Reranker sends to the client and returns canned scores,
    // so we can assert the reordering without a real reranker service.
    private sealed class FakeRerankerClient(float[] scores) : IRerankerClient
    {
        public string? CapturedQuery { get; private set; }
        public List<string>? CapturedTexts { get; private set; }

        public Task<float[]> RankTexts(string query, List<string> texts, CancellationToken cancellationToken = default)
        {
            CapturedQuery = query;
            CapturedTexts = texts;
            return Task.FromResult(scores);
        }
    }

    private static ScoredChunk MakeCandidate(string id, string content) =>
        new(
            new ChunkSearchRecord(
                Id: id,
                SourceId: $"src-{id}",
                CollectionName: "c",
                ChunkIndex: 0,
                ContextualPrefix: null,
                Content: content,
                Distance: 0.0),
            Score: 0.0);

    [Fact]
    public async Task Rank_ReordersCandidatesByRerankScore_Descending()
    {
        var candidates = new List<ScoredChunk>
        {
            MakeCandidate("a", "alpha"),
            MakeCandidate("b", "bravo"),
            MakeCandidate("c", "charlie"),
        };
        // Client scores are aligned to input order: a=0.3, b=0.1, c=0.2.
        var reranker = new Reranker(new FakeRerankerClient([0.3f, 0.1f, 0.2f]));

        var results = await reranker.Rank("q", candidates, CancellationToken.None);

        // Reordered by score descending: a (0.3), c (0.2), b (0.1).
        Assert.Equal(["a", "c", "b"], results.Select(r => r.Chunk.Id).ToArray());
        // The rerank score replaces the fused score on each chunk.
        Assert.Equal((double)0.3f, results[0].Score);
        Assert.Equal((double)0.2f, results[1].Score);
        Assert.Equal((double)0.1f, results[2].Score);
    }

    [Fact]
    public async Task Rank_SendsQueryAndChunkContentToClient_InInputOrder()
    {
        var candidates = new List<ScoredChunk>
        {
            MakeCandidate("a", "alpha"),
            MakeCandidate("b", "bravo"),
        };
        var client = new FakeRerankerClient([0f, 0f]);

        await new Reranker(client).Rank("my query", candidates, CancellationToken.None);

        Assert.Equal("my query", client.CapturedQuery);
        Assert.Equal(["alpha", "bravo"], client.CapturedTexts);
    }

    [Fact]
    public async Task Rank_EmptyInput_ReturnsEmpty()
    {
        var results = await new Reranker(new FakeRerankerClient([])).Rank("q", [], CancellationToken.None);

        Assert.Empty(results);
    }
}
