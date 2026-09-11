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
        var textsForRanking = candidates
            .Select(c => c.Chunk.Content)
            .ToList();

        float[] scores = await _client.RankTexts(
            query, textsForRanking, cancellationToken);

        return [.. candidates
            .Select((c, i) => new ScoredChunk(c.Chunk, scores[i]))
            .OrderByDescending(scored => scored.Score)];
    }
}
