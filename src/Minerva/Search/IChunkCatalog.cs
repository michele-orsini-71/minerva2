using Minerva.Models;

namespace Minerva.Search;

public interface IChunkCatalog
{
    Task<bool> SourceIdExistsAsync(
        string collectionName, string sourceId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ChunkRecord>> GetSourceChunksAsync(
        string collectionName, string sourceId,
        CancellationToken ct = default);
}
