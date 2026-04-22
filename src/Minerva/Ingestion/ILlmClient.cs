namespace Minerva.Ingestion;

public interface ILlmClient : IDisposable
{
    Task<string> GenerateAsync(
        string? systemPrompt, string userPrompt, CancellationToken ct = default);
}
