using Minerva.Models;

namespace Minerva;

public interface ISearchEngine
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        string collectionName,
        SearchOverrides? overrides = null,
        CancellationToken ct = default);

    Task<bool> SourceIdExistsAsync(
        string collectionName,
        string sourceId,
        CancellationToken ct = default);

    Task<Collection?> QueryCollectionInfoAsync(string collectionName, CancellationToken ct = default);

    Task<IReadOnlyList<Collection>> QueryListCollectionsAsync(CancellationToken ct = default);

    Task<SourceText?> GetSourceAsync(
        string collectionName,
        string sourceId,
        CancellationToken ct = default);

    Task<SourceInfo?> GetSourceInfoAsync(
        string collectionName,
        string sourceId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ChunkText>> GetChunkWindowAsync(
        string collectionName,
        string sourceId,
        int chunkIndex,
        int window,
        CancellationToken ct = default);
}
