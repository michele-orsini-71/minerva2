using Minerva;
using Minerva.Models;

namespace Minerva.Search.Bench.Authoring;

public static class AuthoringREPL
{
    public static async Task<int> RunAsync(
        ISearchEngine engine,
        string datasetPath,
        string collection,
        TextWriter output,
        CancellationToken ct)
    {
        output.WriteLine($"Authoring {datasetPath} against collection '{collection}'.");
        output.WriteLine("Commands: :search <q> [--n N], :pick <id|rank>, :undo, :quit  (empty line exits compose mode)");

        string? composeAnchor = null;   // null => top-level; non-null => compose mode anchored to this source-id

        IReadOnlyList<SearchResult> searchResults = [];
        while (!ct.IsCancellationRequested)
        {
            output.Write(composeAnchor is null ? "> " : $"({composeAnchor})> ");
            output.Flush();

            var line = Console.ReadLine();
            if (line is null) break;                       // EOF / Ctrl-D
            if (composeAnchor is not null && line.Length == 0) { composeAnchor = null; continue; }

            if (line.StartsWith(':'))
            {
                var space = line.IndexOf(' ');
                var cmd  = space < 0 ? line : line[..space];
                var rest = space < 0 ? ""   : line[(space + 1)..].Trim();

                switch (cmd)
                {
                    case ":quit": return 0;
                    case ":search": {
                        searchResults = await SearchAsync(output, engine, collection, ct);
                        break;
                    }
                    case ":pick": {
                        await PickAsync(searchResults, output);
                         break;
                    }
                    case ":undo":   /* TODO */ break;
                    default: output.WriteLine($"Unknown command: {cmd}"); break;
                }
            }
            else if (composeAnchor is not null)
            {
                // TODO: append JSONL entry for (composeAnchor, line)
            }
            else
            {
                output.WriteLine("Type :search <query> to find a source.");
            }
        }

        return 0;
    }

    static async Task<IReadOnlyList<SearchResult>> SearchAsync(TextWriter output, ISearchEngine engine, string collection, CancellationToken ct)
    {
        output.Write("Enter the search string to find a source to pick: ");
        output.Flush();
        var line = Console.ReadLine();
        if (line is null)
        {
            return []; 
        }

        var searchResult = await engine.SearchAsync(line, collection, null, ct);

        // dedupe results by source-id, keeping the one with the highest score, and sort by score desc
        var deduped = searchResult.GroupBy(r => r.SourceId)
            .Select(g => g.MaxBy(r => r.Score)!)
            .OrderByDescending(r => r.Score).ToList();

        output.Write($"Found: {deduped.Count} unique sources. Use :pick to pick one.");
        output.Flush();
        return deduped;
    }

    static async Task PickAsync(IReadOnlyList<SearchResult> searchResults, TextWriter output)
    {
        if (searchResults.Count == 0)
        {
            output.WriteLine("No search results to pick from. Use :search to find sources.");
            return;
        }

        for (int i = 0; i < searchResults.Count; i++)
        {
            var searchResult = searchResults[i];
            output.WriteLine($"[{i + 1}] {searchResult.SourceId} {searchResult.Score} {searchResult.Content.Take(100)})");
        }

        output.Write("Enter the rank of the source to pick: ");
        output.Flush();
        var line = Console.ReadLine();
        if (line is null) return;

        var result = searchResults.FirstOrDefault(r => r.SourceId == line);
        if (result is null && int.TryParse(line, out var rank) && rank > 0 && rank <= searchResults.Count)
        {
            result = searchResults[rank - 1];
        }

        if (result is null)
        {
            output.WriteLine($"No search result found with id or rank '{line}'.");
            return;
        }

        output.Write($"Picked: {result.SourceId} {result.Score} {result.Content.Take(100)}.");
        output.Flush();
    }
}
        