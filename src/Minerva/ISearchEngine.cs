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
}
