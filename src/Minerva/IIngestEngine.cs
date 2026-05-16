using Minerva.Models;

namespace Minerva;

public interface IIngestEngine
{
    Task<IngestionResult> IngestAsync(
        string collectionName,
        IAsyncEnumerable<Document> documents,
        bool forceRecreate = false,
        CancellationToken ct = default);
}
