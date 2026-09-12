using Minerva.Models;

namespace Minerva.Ingestion;

internal interface ISourceWriter
{
    Task UpsertSourceAsync(string collectionName, string sourceId, string sourceText,
        IReadOnlyList<ChunkWithEmbedding> chunks, CancellationToken ct = default);

    Task DeleteBySourceIdAsync(string collectionName, string sourceId,
        CancellationToken ct = default);

    Task<string?> GetContentHashAsync(string collectionName, string sourceId,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string>> GetSourceIdsAndHashesAsync(
        string collectionName, CancellationToken ct = default);
}
