using Microsoft.Extensions.AI;

namespace Minerva.Ingestion;

public class DocumentSummarizer : IDocumentSummarizer
{
    private const string SystemPrompt =
        "Summarize the following document in one paragraph. Focus on the main topics, " +
        "key concepts, and structure. This summary will be used to provide context " +
        "when embedding individual chunks of this document.";

    private readonly IChatClient _chatClient;

    public DocumentSummarizer(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string?> SummarizeAsync(string text, CancellationToken ct = default)
    {
        var messages = new ChatMessage[]
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, text),
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text;
    }

    public async Task<IReadOnlyList<string>> SummarizeSegmentsAsync(
        IReadOnlyList<string> segments, CancellationToken ct = default)
    {
        var summaries = new string[segments.Count];

        for (int i = 0; i < segments.Count; i++)
        {
            summaries[i] = await SummarizeAsync(segments[i], ct) ?? string.Empty;
        }

        return summaries;
    }
}
