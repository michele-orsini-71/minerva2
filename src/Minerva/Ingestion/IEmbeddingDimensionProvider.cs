namespace Minerva.Ingestion;

internal interface IEmbeddingDimensionProvider
{
    Task<int> GetDimensionAsync(CancellationToken ct = default);
}
