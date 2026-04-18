using Minerva.Models;

namespace Minerva.Ingestion;

public interface IChunkContextualizer
{
    Task<IReadOnlyList<string>> ContextualizeAsync(
        string documentSummary,
        IReadOnlyList<Chunk> chunks,
        CancellationToken ct = default);
}
