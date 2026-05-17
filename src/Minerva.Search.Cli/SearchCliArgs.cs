using System.Globalization;

namespace Minerva.Search.Cli;

internal enum OutputFormat { Table, Json }

internal sealed record SearchCliArgs(
    string Query,
    IReadOnlyList<string> Collections,
    int? TopK,
    double? Alpha,
    bool? ExpandContext,
    int? CandidatePoolSize,
    OutputFormat Format,
    bool Full,
    int SnippetChars)
{
    public static SearchCliArgs? Parse(string[] args, TextWriter err)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            return null;
        }

        string? query = null;
        var collections = new List<string>();
        int? topK = null;
        double? alpha = null;
        bool? expandContext = null;
        int? candidatePoolSize = null;
        var format = OutputFormat.Table;
        bool full = false;
        int snippetChars = 200;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-c":
                case "--collection":
                    if (++i >= args.Length) { err.WriteLine($"Missing value for {a}"); return null; }
                    collections.Add(args[i]);
                    break;
                case "-k":
                case "--top-k":
                {
                    if (++i >= args.Length || !int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v <= 0)
                    { err.WriteLine($"Invalid value for {a} (expected positive integer)"); return null; }
                    topK = v;
                    break;
                }
                case "-a":
                case "--alpha":
                {
                    if (++i >= args.Length || !double.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0 || v > 1)
                    { err.WriteLine($"Invalid value for {a} (expected 0..1)"); return null; }
                    alpha = v;
                    break;
                }
                case "--expand-context":
                {
                    if (++i >= args.Length || !bool.TryParse(args[i], out var v))
                    { err.WriteLine($"Invalid value for {a} (expected true|false)"); return null; }
                    expandContext = v;
                    break;
                }
                case "--candidate-pool-size":
                {
                    if (++i >= args.Length || !int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v <= 0)
                    { err.WriteLine($"Invalid value for {a} (expected positive integer)"); return null; }
                    candidatePoolSize = v;
                    break;
                }
                case "--format":
                    if (++i >= args.Length) { err.WriteLine($"Missing value for {a}"); return null; }
                    if (!Enum.TryParse<OutputFormat>(args[i], ignoreCase: true, out format))
                    { err.WriteLine($"Invalid format '{args[i]}' (expected: table, json)"); return null; }
                    break;
                case "--full":
                    full = true;
                    break;
                case "--snippet-chars":
                    if (++i >= args.Length || !int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out snippetChars) || snippetChars <= 0)
                    { err.WriteLine($"Invalid value for {a} (expected positive integer)"); return null; }
                    break;
                default:
                    if (a.StartsWith('-'))
                    {
                        err.WriteLine($"Unknown option: {a}");
                        return null;
                    }
                    if (query is not null)
                    {
                        err.WriteLine($"Unexpected positional argument: {a} (query already given)");
                        return null;
                    }
                    query = a;
                    break;
            }
        }

        if (query is null)
        {
            err.WriteLine("Missing query.");
            return null;
        }
        if (collections.Count == 0)
        {
            err.WriteLine("At least one --collection is required.");
            return null;
        }

        return new SearchCliArgs(query, collections, topK, alpha, expandContext, candidatePoolSize, format, full, snippetChars);
    }

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("""
            Usage:
              minerva-search <query> --collection <name> [--collection <name> ...]
                             [--top-k N] [--alpha A]
                             [--expand-context true|false] [--candidate-pool-size N]
                             [--format table|json] [--full] [--snippet-chars 200]

            Options:
              -c, --collection NAME    Collection to search (repeatable, required).
              -k, --top-k N            Overrides Search:TopK from config.
              -a, --alpha A            Overrides Search:HybridAlpha from config (0..1).
                  --expand-context B   Overrides Search:ExpandContext from config (true|false).
                  --candidate-pool-size N
                                       Overrides Search:CandidatePoolSize from config.
                  --format FMT         table | json. Default: table.
                  --full               Print full chunk content (overrides --snippet-chars).
                  --snippet-chars N    Snippet length when not --full. Default: 200.

            Exit codes: 0 = results found, 3 = zero results, 2 = bad args / startup, 1 = unhandled error.
            """);
    }
}
