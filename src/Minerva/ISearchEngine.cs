using Minerva.Models;

namespace Minerva;

public interface ISearchEngine
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        IReadOnlyList<string> collectionNames,
        SearchOptions options,
        CancellationToken ct = default);
}
