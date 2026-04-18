namespace Minerva.Ingestion;

public interface IDocumentSummarizer
{
    Task<string?> SummarizeAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<string>> SummarizeSegmentsAsync(
        IReadOnlyList<string> segments, CancellationToken ct = default);
}
