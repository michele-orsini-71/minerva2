internal sealed record RunBenchArgs(string SweepPath, string OutputDir)
{
    public static RunBenchArgs? Parse(string[] args, TextWriter err)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
            return null;

        string? sweepPath = null;
        string? outputDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-s":
                case "--sweep":
                    if (++i >= args.Length) { err.WriteLine($"Missing value for {a}"); return null; }
                    if (sweepPath is not null) { err.WriteLine($"{a} can only be specified once."); return null; }
                    sweepPath = args[i];
                    break;
                case "-o":
                case "--out":
                    if (++i >= args.Length) { err.WriteLine($"Missing value for {a}"); return null; }
                    if (outputDir is not null) { err.WriteLine($"{a} can only be specified once."); return null; }
                    outputDir = args[i];
                    break;
                default:
                    err.WriteLine($"Unknown option: {a}");
                    return null;
            }
        }

        if (sweepPath is null)
        {
            err.WriteLine("Missing sweep file path.");
            return null;
        }

        if (outputDir is null)
        {
            err.WriteLine("Missing output dir path.");
            return null;
        }

        return new RunBenchArgs(sweepPath, outputDir);
    }

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("""
            Usage:
              minerva-bench run --sweep <sweep-file-path> --out <output-dir-path>

            Runs the benchmark on the given dataset.
            """);
    }
}