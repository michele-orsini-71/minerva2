using System.Reflection;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Search.Bench;
using Minerva.Search.Bench.Authoring;
using Minerva.Search.Bench.Sweep;
using Minerva.Search.Bench.Validation;

try
{
    if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
    {
        PrintTopLevelUsage(Console.Out);
        return args.Length == 0 ? 2 : 0;
    }

    if (args.Contains("-v") || args.Contains("--version"))
    {
        PrintVersion(Console.Out);
        return 0;
    }

    var verb = args[0];
    var verbArgs = args.Skip(1).ToArray();

    switch (verb)
    {
        case "validate-dataset":
            return await RunValidateDatasetAsync(verbArgs);
        case "author-dataset":
            return await RunAuthorDatasetAsync(verbArgs);
        case "run":
            return await RunBenchAsync(verbArgs);
        default:
            Console.Error.WriteLine($"Unknown verb: {verb}");
            PrintTopLevelUsage(Console.Error);
            return 2;
    }
}
catch (OptionsValidationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (MinervaStartupException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (ConfigurationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unhandled exception: {ex}");
    return 1;
}

static async Task<int> RunAuthorDatasetAsync(string[] verbArgs)
{
    var parsed = AuthorDatasetArgs.Parse(verbArgs, Console.Error);
    if (parsed is null)
    {
        AuthorDatasetArgs.PrintUsage(Console.Error);
        return 2;
    }

    var dir = Path.GetDirectoryName(Path.GetFullPath(parsed.DatasetPath));
    if (!Directory.Exists(dir))
    {
        Console.Error.WriteLine($"Directory does not exist: {dir}");
        return 2;
    }

    return await BenchHost.RunAsync(async (engine, cancellationToken) => await AuthorDatasetRunner.RunAsync(
        engine, parsed.DatasetPath, parsed.Collection, Console.Out, cancellationToken));
}

static async Task<int> RunBenchAsync(string[] verbArgs)
{
    var parsed = RunBenchArgs.Parse(verbArgs, Console.Error);
    if (parsed is null)
    {
        RunBenchArgs.PrintUsage(Console.Error);
        return 2;
    }

    var dir = Path.GetDirectoryName(Path.GetFullPath(parsed.OutputDir));
    if (!Directory.Exists(dir))
    {
        Console.Error.WriteLine($"Directory does not exist: {dir}");
        return 2;
    }

    if (!File.Exists(parsed.SweepPath))
    {
        Console.Error.WriteLine($"Sweep file not found: {parsed.SweepPath}");
        return 2;
    }

    return await BenchHost.RunAsync(async (engine, cancellationToken) => await SweepDatasetRunner.RunAsync(
        engine, parsed.SweepPath, parsed.OutputDir, Console.Out, cancellationToken));
}

static async Task<int> RunValidateDatasetAsync(string[] verbArgs)
{
    var parsed = ValidateDatasetArgs.Parse(verbArgs, Console.Error);
    if (parsed is null)
    {
        ValidateDatasetArgs.PrintUsage(Console.Error);
        return 2;
    }

    if (!File.Exists(parsed.DatasetPath))
    {
        Console.Error.WriteLine($"Dataset file not found: {parsed.DatasetPath}");
        return 2;
    }

    return await BenchHost.RunAsync(async (engine, cancellationToken) => await DatasetValidationRunner.RunAsync(
        engine, parsed.DatasetPath, parsed.Collection, Console.Out, cancellationToken));
}

static void PrintVersion(TextWriter w)
{
    w.WriteLine($"{Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"}");
}

static void PrintTopLevelUsage(TextWriter w)
{
    w.WriteLine("""
        Usage:
          minerva-bench <verb> [verb-specific args]

        Verbs:
          validate-dataset    Validate a JSONL eval dataset against an indexed collection.
          author-dataset      Builds a JSONL eval dataset
          run                 Run a sweep and write run.json, metrics.csv, details.jsonl

        Run `minerva-bench <verb> --help` for verb-specific help.

        Exit codes: 0 = success, 2 = bad args / validation failure, 1 = unhandled error.
        """);
}
