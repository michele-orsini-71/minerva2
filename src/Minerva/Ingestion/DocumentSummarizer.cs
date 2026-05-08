using Minerva.Exceptions;

namespace Minerva.Ingestion;

public class DocumentSummarizer : IDocumentSummarizer
{
    private const string SystemPrompt =
        "Summarize the following document in one paragraph. Focus on the main topics, " +
        "key concepts, and structure. This summary will be used to provide context " +
        "when embedding individual chunks of this document.";

    private readonly ILlmClient _llm;

    public DocumentSummarizer(ILlmClient llm)
    {
        _llm = llm;
    }

    public async Task<string?> SummarizeAsync(string text, CancellationToken ct = default)
    {
        try
        {
            return await SummarizeCoreAsync(text, ct);
        }
        catch (ProviderUnavailableException ex)
        {
            throw new ProviderUnavailableException(
                $"Summarization failed (input {text.Length} chars): {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<string>> SummarizeSegmentsAsync(
        IReadOnlyList<string> segments, CancellationToken ct = default)
    {
        var summaries = new string[segments.Count];

        for (int i = 0; i < segments.Count; i++)
        {
            try
            {
                summaries[i] = await SummarizeCoreAsync(segments[i], ct) ?? string.Empty;
            }
            catch (ProviderUnavailableException ex)
            {
                throw new ProviderUnavailableException(
                    $"Summarization failed at segment {i + 1}/{segments.Count} ({segments[i].Length} chars): {ex.Message}", ex);
            }
        }

        return summaries;
    }

    private async Task<string?> SummarizeCoreAsync(string text, CancellationToken ct)
    {
        var result = await _llm.GenerateAsync(SystemPrompt, text, ct);
        return string.IsNullOrEmpty(result) ? null : result;
    }
}
