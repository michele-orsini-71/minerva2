namespace Minerva.Search;

internal class FullTextSearch
{
    private readonly IChunkQuery _chunkQuery;

    public FullTextSearch(IChunkQuery chunkQuery)
    {
        _chunkQuery = chunkQuery;
    }

    public async Task<IReadOnlyList<RankedChunk>> SearchAsync(
        string collectionName,
        string query,
        int topK,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var results = await _chunkQuery.FullTextSearchAsync(
            collectionName, query, topK, ct);

        var ranked = new List<RankedChunk>(results.Count);
        for (int i = 0; i < results.Count; i++)
            ranked.Add(new RankedChunk(results[i], Rank: i + 1));
        return ranked;
    }
}
