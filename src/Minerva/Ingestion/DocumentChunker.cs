using System.Text;
using Markdig;
using Markdig.Syntax;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Utilities;

namespace Minerva.Ingestion;

public class DocumentChunker
{
    private readonly ChunkingOptions _options;

    public DocumentChunker(ChunkingOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Chunks a document into an ordered list of <see cref="Chunk"/> records.
    /// Handles large-document segmentation internally.
    /// </summary>
    public IReadOnlyList<Chunk> Chunk(string collectionName, string sourceId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ChunkingException("Cannot chunk empty or whitespace-only text.");

        var textChunks = SplitIntoChunks(text);
        return BuildChunks(collectionName, sourceId, textChunks, startIndex: 0);
    }

    /// <summary>
    /// Splits a large document into segments at highest-level heading boundaries.
    /// Returns a single-element list for documents under <see cref="ChunkingOptions.LargeDocumentThreshold"/>.
    /// Used by the pipeline when segments need separate summarization.
    /// </summary>
    public IReadOnlyList<string> SegmentDocument(string text)
    {
        if (text.Length <= _options.LargeDocumentThreshold)
            return [text];

        return SplitIntoSegments(text);
    }

    /// <summary>
    /// Chunks a single text segment, starting chunk indices at <paramref name="startIndex"/>.
    /// Used by the pipeline when processing large documents segment-by-segment.
    /// </summary>
    public IReadOnlyList<Chunk> ChunkSegment(
        string collectionName, string sourceId, string text, int startIndex)
    {
        var textChunks = SplitIntoChunks(text);
        return BuildChunks(collectionName, sourceId, textChunks, startIndex);
    }

    private static List<Chunk> BuildChunks(
        string collectionName, string sourceId, List<string> textChunks, int startIndex)
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

    private List<string> SplitIntoChunks(string text)
    {
        // Stage 1: Split at heading boundaries using Markdig
        var sections = SplitByHeaders(text);

        // Stage 2: Split oversized sections recursively
        var result = new List<string>();
        foreach (var section in sections)
        {
            if (section.Length > _options.TargetChunkSize)
                result.AddRange(RecursiveSplit(section, _options.TargetChunkSize, _options.ChunkOverlap));
            else
                result.Add(section);
        }

        return result;
    }

    internal static List<string> SplitByHeaders(string text)
    {
        var document = Markdown.Parse(text);

        var headingStarts = new List<int>();
        foreach (var block in document)
        {
            if (block is HeadingBlock)
                headingStarts.Add(block.Span.Start);
        }

        if (headingStarts.Count == 0)
            return [text.Trim()];

        var sections = new List<string>();

        // Content before first heading
        if (headingStarts[0] > 0)
        {
            var preHeader = text[..headingStarts[0]].Trim();
            if (preHeader.Length > 0)
                sections.Add(preHeader);
        }

        for (int i = 0; i < headingStarts.Count; i++)
        {
            int start = headingStarts[i];
            int end = i + 1 < headingStarts.Count ? headingStarts[i + 1] : text.Length;
            var section = text[start..end].Trim();
            if (section.Length > 0)
                sections.Add(section);
        }

        return sections;
    }

    internal static List<string> RecursiveSplit(string text, int maxSize, int overlap)
    {
        if (text.Length <= maxSize)
            return [text];

        string[] separators = ["\n\n", "\n", " "];

        foreach (var sep in separators)
        {
            var parts = text.Split(sep);
            if (parts.Length <= 1)
                continue;

            var merged = MergeSplitsWithOverlap(parts, sep, maxSize, overlap);

            // Recursively split any still-oversized chunks (using next separator)
            var result = new List<string>();
            foreach (var chunk in merged)
            {
                if (chunk.Length > maxSize)
                    result.AddRange(RecursiveSplit(chunk, maxSize, overlap));
                else
                    result.Add(chunk);
            }
            return result;
        }

        // Last resort: hard character split
        var hardChunks = new List<string>();
        int step = Math.Max(1, maxSize - overlap);
        for (int i = 0; i < text.Length; i += step)
        {
            hardChunks.Add(text.Substring(i, Math.Min(maxSize, text.Length - i)));
            if (i + maxSize >= text.Length) break;
        }
        return hardChunks;
    }

    private static List<string> MergeSplitsWithOverlap(
        string[] splits, string separator, int maxSize, int overlap)
    {
        var result = new List<string>();
        var current = new List<string>();

        int JoinedLength() =>
            current.Count == 0 ? 0
            : current.Sum(s => s.Length) + separator.Length * (current.Count - 1);

        foreach (var split in splits)
        {
            int addLen = current.Count > 0 ? separator.Length + split.Length : split.Length;

            if (JoinedLength() + addLen > maxSize && current.Count > 0)
            {
                result.Add(string.Join(separator, current));

                // Keep trailing parts within overlap budget
                while (current.Count > 0 && JoinedLength() > overlap)
                    current.RemoveAt(0);
            }

            current.Add(split);
        }

        if (current.Count > 0)
            result.Add(string.Join(separator, current));

        return result;
    }

    private List<string> SplitIntoSegments(string text)
    {
        var document = Markdown.Parse(text);

        // Find the highest (lowest-number) heading level present
        int minLevel = int.MaxValue;
        foreach (var block in document)
        {
            if (block is HeadingBlock hb && hb.Level < minLevel)
                minLevel = hb.Level;
        }

        if (minLevel == int.MaxValue)
            return [text]; // No headings — single segment

        // Positions of highest-level headings
        var splitPoints = new List<int>();
        foreach (var block in document)
        {
            if (block is HeadingBlock hb && hb.Level == minLevel)
                splitPoints.Add(block.Span.Start);
        }

        // Extract raw sections at these split points
        var rawSegments = new List<string>();

        if (splitPoints[0] > 0)
        {
            var preHeader = text[..splitPoints[0]].Trim();
            if (preHeader.Length > 0)
                rawSegments.Add(preHeader);
        }

        for (int i = 0; i < splitPoints.Count; i++)
        {
            int start = splitPoints[i];
            int end = i + 1 < splitPoints.Count ? splitPoints[i + 1] : text.Length;
            var section = text[start..end].Trim();
            if (section.Length > 0)
                rawSegments.Add(section);
        }

        // Merge adjacent sections to keep segments under threshold
        var segments = new List<string>();
        var buffer = new StringBuilder();

        foreach (var seg in rawSegments)
        {
            if (buffer.Length > 0 && buffer.Length + 1 + seg.Length > _options.LargeDocumentThreshold)
            {
                segments.Add(buffer.ToString());
                buffer.Clear();
            }

            if (buffer.Length > 0)
                buffer.Append('\n');
            buffer.Append(seg);
        }

        if (buffer.Length > 0)
            segments.Add(buffer.ToString());

        return segments;
    }
}
