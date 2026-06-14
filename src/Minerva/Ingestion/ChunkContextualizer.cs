using Minerva.Models;

namespace Minerva.Ingestion;

public class ChunkContextualizer : IChunkContextualizer
{
    // Bump when PromptTemplate changes: the contextual prefix it produces is
    // embedded, so the prompt text is a content determinant of the stored bytes.
    public const string PromptVersion = "1";

    private const string PromptTemplate =
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

    private readonly ILlmClient _llm;

    public ChunkContextualizer(ILlmClient llm)
    {
        _llm = llm;
    }

    public async Task<IReadOnlyList<string>> ContextualizeAsync(
        string documentSummary,
        IReadOnlyList<Chunk> chunks,
        CancellationToken ct = default)
    {
        var prefixes = new string[chunks.Count];

        for (int i = 0; i < chunks.Count; i++)
        {
            var prompt = string.Format(PromptTemplate, documentSummary, chunks[i].Content);
            prefixes[i] = await _llm.GenerateAsync(systemPrompt: null, prompt, ct);
        }

        return prefixes;
    }
}
