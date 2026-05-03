using Minerva.Models;

namespace Minerva;

public interface IMinervaEngine
{
    Task<IngestionResult> IngestAsync(
        string collectionName,
        IAsyncEnumerable<Document> documents,
        bool forceRecreate = false,
        CancellationToken ct = default);

    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        IReadOnlyList<string> collectionNames,
        SearchOptions? options = null,
        CancellationToken ct = default);
}
