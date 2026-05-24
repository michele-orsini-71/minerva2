using Minerva;
using Minerva.Models;
using Minerva.Search.Bench.Common;

namespace Minerva.Search.Bench.Authoring;

public static class AuthoringEngine
{
    public static async Task<int> RunAsync(
        ISearchEngine engine,
        string datasetPath,
        string collection,
        IList<ParsedEntry> entries,
        TextWriter output,
        CancellationToken ct)
    {
        output.WriteLine($"Authoring {datasetPath} against collection '{collection}'.");

        output.Write("Enter the search string to find a source to pick: ");
        output.Flush();
        var line = Console.ReadLine();
        if (line is null)
        {
            return 1;
        }

        var search = await engine.SearchAsync(line, collection, null, ct);

        // dedupe results by source-id, keeping the one with the highest score, and sort by score desc
        IReadOnlyList<SearchResult> searchResults = search.GroupBy(r => r.SourceId)
            .Select(g => g.MaxBy(r => r.Score)!)
            .OrderByDescending(r => r.Score).ToList();

        output.Write($"Found: {searchResults.Count} unique sources.\n");
        output.Flush();

        if (searchResults.Count == 0)
        {
            return 1;
        }

        for (int i = 0; i < searchResults.Count; i++)
        {
            var searchResult = searchResults[i];
            output.WriteLine($"[{i + 1}] {searchResult.SourceId} {searchResult.Score}\n{searchResult.Content[..Math.Min(100, searchResult.Content.Length)]}");
        }

        output.Write("Enter the rank of the source to pick: ");
        output.Flush();
        line = Console.ReadLine();
        if (line is null) return 1;

        SearchResult? result = null;
        if (int.TryParse(line, out var rank) && rank > 0 && rank <= searchResults.Count)
        {
            result = searchResults[rank - 1];
        }

        if (result is null)
        {
            output.WriteLine($"Invalid rank '{line}'. Expected 1..{searchResults.Count}.");
            return 1;
        }

        output.WriteLine($"Picked: {result.SourceId} {result.Score} {result.Content[..Math.Min(100, result.Content.Length)]}.");
        output.Flush();

        var ids = new HashSet<string>(entries.Select(e => e.Id));

        List<(string Id, string Query)> newEntries = [];
        while (true)
        {
            output.Write("Enter the query for the dataset: ");
            output.Flush();
            line = Console.ReadLine();
            if (line is null || line.Length == 0)
            {
                break;
            }

            var newDatasetID = DatasetIdGenerator.GenerateId(result.SourceId, ids);
            ids.Add(newDatasetID);
            newEntries.Add((newDatasetID, line));
        }

        List<string> lines = [];
        foreach (var entry in newEntries)
        {
            var jsonLine = System.Text.Json.JsonSerializer.Serialize(
                new { id = entry.Id, query = entry.Query, gold_sources = new[] { result.SourceId } });
            lines.Add(jsonLine);
        }

        if (lines.Count == 0)
        {
            output.WriteLine("No new entries to add.");
            return 0;
        }

        if (File.Exists(datasetPath))
        {
            var fileContent = File.ReadAllText(datasetPath);
            if (fileContent.Length > 0 && !fileContent.EndsWith("\n"))
            {
                File.AppendAllText(datasetPath, "\n");
            }
        }

        File.AppendAllLines(datasetPath, lines);

        return 0;
    }
}
