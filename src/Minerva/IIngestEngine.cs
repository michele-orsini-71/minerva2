using Minerva.Models;

namespace Minerva;

public interface IIngestEngine
{
    Task<IngestionResult> IngestAsync(
        string collectionName,
        IAsyncEnumerable<Document> documents,
        bool allowRecreateOnConfigMismatch = false,
        CancellationToken ct = default);
}
