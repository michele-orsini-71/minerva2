using Microsoft.Extensions.AI;
using Minerva.Models;

namespace Minerva.Ingestion;

public class ChunkContextualizer
{
    private const string SystemPrompt =
        """
        <document>
        {0}
        </document>
        Here is the chunk we want to situate within the whole document:
        <chunk>
        {1}
        </chunk>
        Please give a short succinct context to situate this chunk within the overall
        document for the purposes of improving search retrieval of the chunk.
        Answer only with the succinct context and nothing else.
        """;

    private readonly IChatClient _chatClient;

    public ChunkContextualizer(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<IReadOnlyList<string>> ContextualizeAsync(
        string documentSummary,
        IReadOnlyList<Chunk> chunks,
        CancellationToken ct = default)
    {
        var prefixes = new string[chunks.Count];

        for (int i = 0; i < chunks.Count; i++)
        {
            var prompt = string.Format(SystemPrompt, documentSummary, chunks[i].Content);

            var messages = new ChatMessage[]
            {
                new(ChatRole.User, prompt),
            };

            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
            prefixes[i] = response.Text ?? string.Empty;
        }

        return prefixes;
    }
}
