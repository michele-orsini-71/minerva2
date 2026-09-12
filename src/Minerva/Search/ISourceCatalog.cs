namespace Minerva.Search;

internal interface ISourceCatalog
{
    Task<bool> SourceIdExistsAsync(
        string collectionName, string sourceId,
        CancellationToken ct = default);

    Task<string?> GetSourceTextAsync(
        string collectionName, string sourceId,
        CancellationToken ct = default);
}
