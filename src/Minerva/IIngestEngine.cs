using Minerva.Models;

namespace Minerva;

public interface IIngestEngine
{
    Task<IngestionResult> IngestAsync(
        string collectionName,
        ClientProvenance clientProvenance,
        IAsyncEnumerable<Document> documents,
        bool allowRecreateOnConfigMismatch = false,
        CancellationToken ct = default);

    Task<Collection?> QueryCollectionInfoAsync(string collectionName, CancellationToken ct = default);
}
