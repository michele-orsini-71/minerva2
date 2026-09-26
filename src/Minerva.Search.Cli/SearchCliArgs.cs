using System.Globalization;
using System.Reflection;
using Minerva.Utils;

namespace Minerva.Search.Cli;

internal enum OutputFormat { Table, Json }

internal sealed record SearchCliArgs(
    string Query,
    string Collection,
    int? TopK,
    double? Alpha,
    bool? ExpandContext,
    bool? EnableReranker,
    int? RerankDepth,
    int? CascadeDepth,
    int? CandidatePoolSize,
    OutputFormat Format,
    bool Full,
    int SnippetChars,
    bool version)
{
    public static SearchCliArgs? Parse(string[] args, TextWriter err)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            return null;
        }

        string? query = null;
        string? collection = null;
        int? topK = null;
        double? alpha = null;
        bool? expandContext = null;
        bool? enableReranker = null;
        int? candidatePoolSize = null;
        int? rerankDepth = null;
        int? cascadeDepth = null;
        var format = OutputFormat.Table;
        bool full = false;
        int snippetChars = 200;
        bool versionAsked = false;

        if (args.Contains("--version") || args.Contains("-v"))
        {
            versionAsked = true;
            return new SearchCliArgs("", "", topK, alpha, expandContext, enableReranker, rerankDepth, cascadeDepth, candidatePoolSize, format, full, snippetChars, versionAsked);
        }

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-c":
                case "--collection":
                    if (++i >= args.Length) { err.WriteLine($"Missing value for {a}"); return null; }
                    if (collection is not null) { err.WriteLine($"{a} can only be specified once."); return null; }
                    collection = args[i];
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
                case "--rerank":
                {
                    if (++i >= args.Length || !bool.TryParse(args[i], out var v))
                    { err.WriteLine($"Invalid value for {a} (expected true|false)"); return null; }
                    enableReranker = v;
                    break;
                }
                case "--candidate-pool-size":
                {
                    if (++i >= args.Length || !int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v <= 0)
                    { err.WriteLine($"Invalid value for {a} (expected positive integer)"); return null; }
                    candidatePoolSize = v;
                    break;
                }
                case "--rerank-depth":
                {
                    if (++i >= args.Length || !int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v <= 0)
                    { err.WriteLine($"Invalid value for {a} (expected positive integer)"); return null; }
                    rerankDepth = v;
                    break;
                }
                case "--cascade-depth":
                {
                    if (++i >= args.Length || !int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v <= 0)
                    { err.WriteLine($"Invalid value for {a} (expected positive integer)"); return null; }
                    cascadeDepth = v;
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
                case "--version":

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
        if (collection is null)
        {
            err.WriteLine("--collection is required.");
            return null;
        }

        return new SearchCliArgs(query, collection, topK, alpha, expandContext, enableReranker, 
            rerankDepth, cascadeDepth, candidatePoolSize, format, full, snippetChars, versionAsked);
    }
    public static void PrintVersion(TextWriter w)
    {
        VersionInfo.PrintVersion(w, Assembly.GetExecutingAssembly());
    }

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("""
            Usage:
              minerva-search <query> --collection <name>
                             [--top-k N] [--alpha A]
                             [--expand-context true|false] [--rerank true|false]
                             [--candidate-pool-size N] [--rerank-depth N] [--cascade-depth N]
                             [--format table|json] [--full] [--snippet-chars 200]
              minerva-search --version | -v
              minerva-search --help | -h

            Options:
              -c, --collection NAME    Collection to search (required).
              -k, --top-k N            Overrides Search:TopK from config.
              -a, --alpha A            Overrides Search:HybridAlpha from config (0..1).
                  --expand-context B   Overrides Search:ExpandContext from config (true|false).
                  --rerank B           Overrides Search:EnableReranker from config (true|false).
                  --candidate-pool-size N
                                       Overrides Search:CandidatePoolSize from config.
                  --rerank-depth N     Overrides Search:RerankDepth from config: how many fused
                                       candidates the reranker scores. Unset = all of them.
                  --cascade-depth N    Overrides Search:CascadeDepth from config: how many of the
                                       reranked candidates the cascade reranker rescores.
                                       Needs Minerva:CascadeReranker configured; unset = no cascade.
                  --format FMT         table | json. Default: table.
                  --full               Print full chunk content (overrides --snippet-chars).
                  --snippet-chars N    Snippet length when not --full. Default: 200.
              -v, --version            Print the version and exit.
              -h, --help               Print this help and exit.

            Exit codes: 0 = results found, 3 = zero results, 2 = bad args / startup, 1 = unhandled error.
            """);
    }
}
