namespace Minerva.Ingestion;

public interface IEmbeddingDimensionProvider
{
    Task<int> GetDimensionAsync(CancellationToken ct = default);
}
