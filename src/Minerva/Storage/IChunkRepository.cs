namespace Minerva.Storage;

public interface IChunkRepository
{
    Task UpsertChunksAsync(string collectionName, string sourceId,
        IReadOnlyList<ChunkWithEmbedding> chunks, CancellationToken ct = default);

    Task DeleteBySourceIdAsync(string collectionName, string sourceId,
        CancellationToken ct = default);

    Task<string?> GetContentHashAsync(string collectionName, string sourceId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ChunkRecord>> GetAdjacentChunksAsync(
        IReadOnlyList<string> chunkIds, CancellationToken ct = default);

    Task<IReadOnlyList<ChunkSearchRecord>> VectorSearchAsync(
        string collectionName, float[] queryEmbedding, int topK,
        CancellationToken ct = default);

    Task<IReadOnlyList<ChunkSearchRecord>> FullTextSearchAsync(
        string collectionName, string query, int topK,
        CancellationToken ct = default);
}
