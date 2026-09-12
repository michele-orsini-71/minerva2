using Minerva.Models;

namespace Minerva.Search;

internal interface IChunkQuery
{
    Task<IReadOnlyList<ChunkRecord>> GetAdjacentChunksAsync(
        IReadOnlyList<string> chunkIds, CancellationToken ct = default);

    Task<IReadOnlyList<ChunkSearchRecord>> VectorSearchAsync(
        string collectionName, float[] queryEmbedding, int topK,
        CancellationToken ct = default);

    Task<IReadOnlyList<ChunkSearchRecord>> FullTextSearchAsync(
        string collectionName, string query, int topK,
        CancellationToken ct = default);
}
