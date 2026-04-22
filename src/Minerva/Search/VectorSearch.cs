namespace Minerva.Search;

public class VectorSearch
{
    private readonly IChunkQuery _chunkQuery;

    public VectorSearch(IChunkQuery chunkQuery)
    {
        _chunkQuery = chunkQuery;
    }

    public async Task<IReadOnlyList<RankedChunk>> SearchAsync(
        string collectionName,
        float[] queryEmbedding,
        int topK,
        CancellationToken ct = default)
    {
        var results = await _chunkQuery.VectorSearchAsync(
            collectionName, queryEmbedding, topK, ct);

        var ranked = new List<RankedChunk>(results.Count);
        for (int i = 0; i < results.Count; i++)
            ranked.Add(new RankedChunk(results[i], Rank: i + 1));
        return ranked;
    }
}
