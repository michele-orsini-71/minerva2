using Minerva.Models;

namespace Minerva.Search;

internal interface ISourceCatalog
{
    Task<bool> SourceIdExistsAsync(
        string collectionName, string sourceId,
        CancellationToken ct = default);

    Task<string?> GetSourceTextAsync(
        string collectionName, string sourceId,
        CancellationToken ct = default);

    Task<SourceInfo?> GetSourceInfoAsync(
        string collectionName, string sourceId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ChunkRecord>> GetChunkRangeAsync(
        string collectionName, string sourceId, int fromIndex, int toIndex,
        CancellationToken ct = default);
}
