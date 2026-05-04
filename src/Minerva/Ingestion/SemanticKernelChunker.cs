#pragma warning disable SKEXP0050 // TextChunker is experimental in Semantic Kernel

using Microsoft.SemanticKernel.Text;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Utilities;

namespace Minerva.Ingestion;

public class SemanticKernelChunker : IDocumentChunker
{
    private readonly ChunkingOptions _options;

    public SemanticKernelChunker(ChunkingOptions options)
    {
        _options = options;
    }

    public IReadOnlyList<Chunk> Chunk(string collectionName, string sourceId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ChunkingException("Cannot chunk empty or whitespace-only text.");

        var textChunks = SplitMarkdown(text);
        return BuildChunks(collectionName, sourceId, textChunks, startIndex: 0);
    }

    public IReadOnlyList<Chunk> ChunkSegment(
        string collectionName, string sourceId, string text, int startIndex)
    {
        var textChunks = SplitMarkdown(text);
        return BuildChunks(collectionName, sourceId, textChunks, startIndex);
    }

    public IReadOnlyList<string> SegmentDocument(string text) => [text];

    private List<string> SplitMarkdown(string text)
    {
        // Use a char counter so the limits map directly to ChunkingOptions (which are char-based).
        // This makes the SK-vs-custom comparison apples-to-apples.
        static int CharCounter(string s) => s.Length;

        var lines = TextChunker.SplitMarkDownLines(
            text, maxTokensPerLine: _options.TargetChunkSize / 2, tokenCounter: CharCounter);

        return TextChunker.SplitMarkdownParagraphs(
            lines,
            maxTokensPerParagraph: _options.TargetChunkSize,
            overlapTokens: _options.ChunkOverlap,
            chunkHeader: null,
            tokenCounter: CharCounter);
    }

    private static List<Chunk> BuildChunks(
        string collectionName, string sourceId, IReadOnlyList<string> textChunks, int startIndex)
    {
        var chunks = new List<Chunk>(textChunks.Count);
        for (int i = 0; i < textChunks.Count; i++)
        {
            int index = startIndex + i;
            chunks.Add(new Chunk(
                Id: HashHelper.GenerateChunkId(sourceId, index),
                SourceId: sourceId,
                CollectionName: collectionName,
                ChunkIndex: index,
                Content: textChunks[i],
                ContentHash: HashHelper.ComputeContentHash(textChunks[i])));
        }
        return chunks;
    }
}
