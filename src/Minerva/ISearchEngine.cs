using Minerva.Models;

namespace Minerva;

public interface ISearchEngine
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        IReadOnlyList<string> collectionNames,
        SearchOverrides? overrides = null,
        CancellationToken ct = default);
}
