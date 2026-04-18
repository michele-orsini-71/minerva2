using Minerva.Collections;
using Minerva.Models;

namespace Minerva;

public interface IMinervaEngine
{
    ICollectionService Collections { get; }

    Task<IngestionResult> IngestAsync(
        string collectionName,
        Document document,
        CancellationToken ct = default);

    Task RemoveAsync(
        string collectionName,
        string sourceId,
        CancellationToken ct = default);

    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        IReadOnlyList<string> collectionNames,
        SearchOptions? options = null,
        CancellationToken ct = default);
}
