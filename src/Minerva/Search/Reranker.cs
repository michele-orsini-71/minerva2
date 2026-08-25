namespace Minerva.Search;

class Reranker : IReranker
{
    IRerankerClient _client;

    public Reranker(IRerankerClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<ScoredChunk>> Rank(
        string query, IReadOnlyList<ScoredChunk> candidates, CancellationToken cancellationToken)
    {
        float[] scores = await _client.RankTexts(
            query, [.. candidates.Select(c => c.Chunk.Content)], cancellationToken);

        return [.. candidates
            .Select((c, i) => new ScoredChunk(c.Chunk, scores[i]))
            .OrderByDescending(scored => scored.Score)];
    }
}
