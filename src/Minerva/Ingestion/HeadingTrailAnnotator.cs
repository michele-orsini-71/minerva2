using Markdig;
using Markdig.Syntax;
using Minerva.Models;

namespace Minerva.Ingestion;

public static class HeadingTrailAnnotator
{
    public static List<Chunk> Annotate(IReadOnlyList<Chunk> chunks)
    {
        var stack = new List<(int Level, string Text)>();
        var result = new List<Chunk>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var (headings, leadingCount) = ExtractHeadings(chunk.Content);

            // A chunk that opens with headings belongs to their section: its body
            // text lives under the whole leading run (e.g. "# Doc\n## Section\ntext").
            var effective = new List<(int Level, string Text)>(stack);
            foreach (var heading in headings.Take(leadingCount))
                Apply(effective, heading);

            result.Add(chunk with { HeadingTrail = effective.Select(h => h.Text).ToList() });

            // Re-seeing a heading (chunk overlap, repeated section prefixes) is
            // idempotent: pop-then-push of the same entry leaves the stack unchanged.
            foreach (var heading in headings)
                Apply(stack, heading);
        }

        return result;
    }

    private static void Apply(List<(int Level, string Text)> stack, (int Level, string Text) heading)
    {
        stack.RemoveAll(h => h.Level >= heading.Level);
        stack.Add(heading);
    }

    private static (List<(int Level, string Text)> Headings, int LeadingCount) ExtractHeadings(string content)
    {
        var headings = new List<(int, string)>();
        int leadingCount = 0;
        bool stillLeading = true;

        foreach (var block in Markdown.Parse(content))
        {
            if (block is not HeadingBlock heading)
            {
                stillLeading = false;
                continue;
            }

            // Take the heading's source text: first line of the span (covers both
            // ATX '## Title' and setext 'Title\n====='), stripped of markers.
            var span = content.Substring(heading.Span.Start, heading.Span.Length);
            int newline = span.IndexOf('\n');
            var line = (newline >= 0 ? span[..newline] : span).TrimEnd('\r');
            var text = line.TrimStart('#').Trim();

            if (text.Length == 0)
                continue;

            headings.Add((heading.Level, text));
            if (stillLeading)
                leadingCount++;
        }

        return (headings, leadingCount);
    }
}
