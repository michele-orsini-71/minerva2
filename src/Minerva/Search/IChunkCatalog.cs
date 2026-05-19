namespace Minerva.Search;

public interface IChunkCatalog
{
    Task<bool> SourceIdExistsAsync(
        string collectionName, string sourceId,
        CancellationToken ct = default);
}
