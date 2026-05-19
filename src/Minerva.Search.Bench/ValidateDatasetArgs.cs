namespace Minerva.Search.Bench;

internal sealed record ValidateDatasetArgs(string DatasetPath, string Collection)
{
    public static ValidateDatasetArgs? Parse(string[] args, TextWriter err)
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

        return new ValidateDatasetArgs(datasetPath, collection);
    }

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("""
            Usage:
              minerva-bench validate-dataset <jsonl-path> --collection <name>

            Validates that every gold_source in the dataset resolves to a chunk in
            the named collection, and that the dataset itself conforms to the
            schema (required fields, unique ids, no Phase-4 reserved fields, etc.).

            Options:
              -c, --collection NAME    Collection to resolve gold_sources against (required).

            Exit codes: 0 = valid, 2 = validation failed / bad args / startup, 1 = unhandled error.
            """);
    }
}
