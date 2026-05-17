using Minerva.Models;

namespace Minerva;

public interface ISearchEngine
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        string collectionName,
        SearchOverrides? overrides = null,
        CancellationToken ct = default);
}
