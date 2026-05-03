using Minerva.Models;

namespace Minerva.Ingestion;

public interface IChunkWriter
{
    Task UpsertChunksAsync(string collectionName, string sourceId,
        IReadOnlyList<ChunkWithEmbedding> chunks, CancellationToken ct = default);

    Task DeleteBySourceIdAsync(string collectionName, string sourceId,
        CancellationToken ct = default);

    Task<string?> GetContentHashAsync(string collectionName, string sourceId,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string>> GetSourceIdsAndHashesAsync(
        string collectionName, CancellationToken ct = default);
}
