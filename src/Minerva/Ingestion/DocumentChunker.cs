using System.Text;
using Markdig;
using Markdig.Syntax;
using Microsoft.Extensions.Logging;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Utilities;

namespace Minerva.Ingestion;

public class DocumentChunker : IDocumentChunker
{
    private readonly ChunkingOptions _options;
    private readonly int _maxSegmentChars;
    private readonly ILogger<DocumentChunker>? _logger;

    public DocumentChunker(ChunkingOptions options, ILogger<DocumentChunker>? logger = null)
    {
        _options = options;
        _maxSegmentChars = ComputeMaxSegmentChars(options.ContextBudget);
        _logger = logger;
    }

    /// <summary>
    /// Translates a token-denominated context budget into the char ceiling for one
    /// LLM input. Lives here (not in the binder) because the chars↔tokens bridge
    /// is the chunker's concern; the binder only validates the inputs.
    /// </summary>
    internal static int ComputeMaxSegmentChars(ContextBudgetOptions budget)
    {
        double chars = (budget.MaxContextTokens - budget.ReservedTokens)
            * budget.CharsPerToken
            * budget.SafetyFactor;
        return (int)Math.Floor(chars);
    }

    /// <summary>
    /// Chunks a document into an ordered list of <see cref="Chunk"/> records.
    /// Handles large-document segmentation internally.
    /// </summary>
    public IReadOnlyList<Chunk> Chunk(string collectionName, string sourceId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ChunkingException("Cannot chunk empty or whitespace-only text.");

        var textChunks = SplitMarkdownToBudget(text, _options.TargetChunkSize, _options.ChunkOverlap);
        return BuildChunks(collectionName, sourceId, textChunks, startIndex: 0);
    }

    /// <summary>
    /// Splits a large document into segments under the char budget derived from
    /// <see cref="ChunkingOptions.ContextBudget"/>. Used by the pipeline when segments
    /// need separate summarization (e.g. an LLM context budget).
    /// </summary>
    public IReadOnlyList<string> SegmentDocument(string text)
    {
        return SplitMarkdownToBudget(text, _maxSegmentChars, overlap: 0);
    }

    /// <summary>
    /// Chunks a single text segment, starting chunk indices at <paramref name="startIndex"/>.
    /// Used by the pipeline when processing large documents segment-by-segment.
    /// </summary>
    public IReadOnlyList<Chunk> ChunkSegment(
        string collectionName, string sourceId, string text, int startIndex)
    {
        var textChunks = SplitMarkdownToBudget(text, _options.TargetChunkSize, _options.ChunkOverlap);
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
                Id: HashHelper.GenerateChunkId(collectionName, sourceId, index),
                SourceId: sourceId,
                CollectionName: collectionName,
                ChunkIndex: index,
                Content: textChunks[i],
                ContentHash: HashHelper.ComputeContentHash(textChunks[i])));
        }
        return chunks;
    }

    /// <summary>
    /// Unified core: splits markdown into pieces each ≤ <paramref name="maxChars"/>.
    /// Used both for chunker output (embedder budget) and segmenter output (LLM budget).
    /// Algorithm: header-aware split → greedy pack → recursive separator fallback →
    /// brute-force slice with overlap. Returns <c>[text]</c> verbatim when input fits.
    /// </summary>
    private List<string> SplitMarkdownToBudget(string text, int maxChars, int overlap)
    {
        if (text.Length <= maxChars)
            return [text];

        var sections = SplitByHeaders(text);

        var chunks = new List<string>();
        var current = new StringBuilder();
        const string sep = "\n\n";

        foreach (var section in sections)
        {
            if (section.Length > maxChars)
            {
                string? extraPrefix = null;
                if (current.Length > 0)
                {
                    var currentStr = current.ToString();
                    if (IsHeaderOnly(currentStr))
                        extraPrefix = currentStr;
                    else
                        chunks.Add(currentStr);
                    current.Clear();
                }
                chunks.AddRange(SplitOversizedSection(section, maxChars, overlap, extraPrefix));
            }
            else if (current.Length > 0 && current.Length + sep.Length + section.Length > maxChars)
            {
                chunks.Add(current.ToString());
                current.Clear();
                current.Append(section);
            }
            else
            {
                if (current.Length > 0) current.Append(sep);
                current.Append(section);
            }
        }

        if (current.Length > 0)
            chunks.Add(current.ToString());

        // Tail absorption: fold a too-small final chunk into its predecessor, but only
        // when the merged result still fits. Skipping this when over budget preserves
        // the ≤-budget contract for documents whose tail would otherwise overflow.
        int minChunkSize = maxChars / 4;
        if (chunks.Count >= 2 && chunks[^1].Length < minChunkSize)
        {
            int merged = chunks[^2].Length + 2 + chunks[^1].Length;
            if (merged <= maxChars)
            {
                chunks[^2] = chunks[^2] + "\n\n" + chunks[^1];
                chunks.RemoveAt(chunks.Count - 1);
            }
        }

        return chunks;
    }

    private List<string> SplitOversizedSection(
        string section, int maxChars, int overlap, string? extraPrefix = null)
    {
        var (heading, body) = SplitHeadingFromBody(section);

        var combinedPrefix = (extraPrefix, heading) switch
        {
            (not null, not null) => extraPrefix + "\n\n" + heading,
            (not null, null) => extraPrefix,
            (null, not null) => heading,
            _ => null,
        };

        if (combinedPrefix is null)
            return RecursiveSplit(body, maxChars, overlap, _logger);

        int bodyMaxSize = maxChars - combinedPrefix.Length - 2;
        if (bodyMaxSize < maxChars / 2)
            bodyMaxSize = maxChars / 2;

        var bodyChunks = RecursiveSplit(body, bodyMaxSize, overlap, _logger);
        return bodyChunks.Select(c => combinedPrefix + "\n\n" + c).ToList();
    }

    private static (string? heading, string body) SplitHeadingFromBody(string section)
    {
        if (!section.StartsWith('#'))
            return (null, section);

        int newlinePos = section.IndexOf('\n');
        if (newlinePos < 0)
            return (section, string.Empty);

        var heading = section[..newlinePos].TrimEnd('\r');
        var body = section[(newlinePos + 1)..].TrimStart();
        return (heading, body);
    }

    private static bool IsHeaderOnly(string section)
    {
        foreach (var line in section.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (!trimmed.StartsWith('#')) return false;
        }
        return true;
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

    internal static List<string> RecursiveSplit(string text, int maxSize, int overlap, ILogger? logger = null)
    {
        if (text.Length <= maxSize)
            return [text];

        string[] separators = ["\n\n", "\n", ". ", "; ", " "];

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
                    result.AddRange(RecursiveSplit(chunk, maxSize, overlap, logger));
                else
                    result.Add(chunk);
            }
            return result;
        }

        // No usable separator anywhere — brute-force slice with overlap. Slicing
        // mid-token is ugly but the ≤-budget contract is more important than token
        // alignment; downstream embedding/LLM calls would reject an oversized blob.
        return BruteForceSlice(text, maxSize, overlap, logger);
    }

    private static List<string> BruteForceSlice(string text, int maxSize, int overlap, ILogger? logger)
    {
        logger?.LogWarning(
            "Could not split a {Length}-char section within maxSize={Max}; brute-force slicing with overlap={Overlap}",
            text.Length, maxSize, overlap);

        int step = Math.Max(1, maxSize - overlap);
        var result = new List<string>();
        for (int i = 0; i < text.Length; i += step)
        {
            int end = Math.Min(text.Length, i + maxSize);
            result.Add(text[i..end]);
            if (end == text.Length) break;
        }
        return result;
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

            // If the split is too big to fit alongside even the trimmed carry, drop
            // the carry so the split stands on its own. The carry was already emitted
            // by the flush above; re-emitting it here would duplicate.
            if (current.Count > 0 && JoinedLength() + separator.Length + split.Length > maxSize)
                current.Clear();

            current.Add(split);
        }

        if (current.Count > 0)
            result.Add(string.Join(separator, current));

        return result;
    }

}
