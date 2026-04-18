using Minerva.Storage;

namespace Minerva.Search;

public class VectorSearch
{
    private readonly IChunkRepository _chunkRepository;

    public VectorSearch(IChunkRepository chunkRepository)
    {
        _chunkRepository = chunkRepository;
    }

    public async Task<IReadOnlyList<RankedChunk>> SearchAsync(
        string collectionName,
        float[] queryEmbedding,
        int topK,
        CancellationToken ct = default)
    {
        var results = await _chunkRepository.VectorSearchAsync(
            collectionName, queryEmbedding, topK, ct);

        var ranked = new List<RankedChunk>(results.Count);
        for (int i = 0; i < results.Count; i++)
            ranked.Add(new RankedChunk(results[i], Rank: i + 1));
        return ranked;
    }
}
