namespace Minerva.Ingestion;

public interface IEmbeddingClient : IDisposable
{
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);
}
