namespace Minerva.Ingestion;

public interface IEmbeddingService
{
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}
