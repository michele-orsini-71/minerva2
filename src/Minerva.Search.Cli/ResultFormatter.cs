using System.Globalization;
using System.Text.Json;
using Minerva.Models;

namespace Minerva.Search.Cli;

internal static class ResultFormatter
{
    public static void Print(IReadOnlyList<SearchResult> results, SearchCliArgs args, TextWriter w)
    {
        switch (args.Format)
        {
            case OutputFormat.Json:
                PrintJson(results, w);
                break;
            case OutputFormat.Table:
            default:
                PrintTable(results, args, w);
                break;
        }
    }

    private static void PrintJson(IReadOnlyList<SearchResult> results, TextWriter w)
    {
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        w.WriteLine(json);
    }

    private static void PrintTable(IReadOnlyList<SearchResult> results, SearchCliArgs args, TextWriter w)
    {
        if (results.Count == 0)
        {
            w.WriteLine("(no results)");
            return;
        }

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            string score = r.Score.ToString("F4", CultureInfo.InvariantCulture);
            w.WriteLine($"#{i + 1,-3} score={score}  [{r.CollectionName}] {r.SourceId}");

            string body = args.Full ? r.Content : Snippet(r.Content, args.SnippetChars);
            foreach (var line in body.Split('\n'))
                w.WriteLine($"    {line.TrimEnd('\r')}");

            if (r.ContextBefore is not null || r.ContextAfter is not null)
            {
                if (r.ContextBefore is not null)
                    w.WriteLine($"    [before] {Snippet(r.ContextBefore, 120)}");
                if (r.ContextAfter is not null)
                    w.WriteLine($"    [after]  {Snippet(r.ContextAfter, 120)}");
            }

            w.WriteLine();
        }
    }

    private static string Snippet(string s, int max)
    {
        s = s.Replace("\r\n", "\n").Trim();
        if (s.Length <= max) return s;
        return s[..max] + "…";
    }
}
