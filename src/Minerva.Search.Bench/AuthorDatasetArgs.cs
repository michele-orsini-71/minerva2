namespace Minerva.Search.Bench;

internal sealed record AuthorDatasetArgs(string DatasetPath, string Collection)
{
    public static AuthorDatasetArgs? Parse(string[] args, TextWriter err)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
            return null;

        string? datasetPath = null;
        string? collection = null;

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
                default:
                    if (a.StartsWith('-'))
                    {
                        err.WriteLine($"Unknown option: {a}");
                        return null;
                    }
                    if (datasetPath is not null)
                    {
                        err.WriteLine($"Unexpected positional argument: {a} (dataset path already given)");
                        return null;
                    }
                    datasetPath = a;
                    break;
            }
        }

        if (datasetPath is null)
        {
            err.WriteLine("Missing dataset path.");
            return null;
        }
        if (collection is null)
        {
            err.WriteLine("--collection is required.");
            return null;
        }

        return new AuthorDatasetArgs(datasetPath, collection);
    }

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("""
            Usage:
              minerva-bench author-dataset <jsonl-path> --collection <name>

            Helps authoring the dataset with a REPL session.

            Options:
              -c, --collection NAME    Collection to resolve gold_sources against (required).

            Exit codes: 0 = valid, 2 = validation failed / bad args / startup, 1 = unhandled error.
            """);
    }
}
