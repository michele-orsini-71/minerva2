using System.Reflection;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Search.Bench;
using Minerva.Search.Bench.Authoring;
using Minerva.Search.Bench.Sweep;
using Minerva.Search.Bench.Validation;

try
{
    // --config applies to every verb, so it is taken out before the verb parsers see the args.
    string? configPath = null;
    var configIndex = Array.IndexOf(args, "--config");
    if (configIndex >= 0)
    {
        if (configIndex + 1 >= args.Length)
        {
            Console.Error.WriteLine("Missing value for --config");
            return 2;
        }
        configPath = args[configIndex + 1];
        args = [.. args[..configIndex], .. args[(configIndex + 2)..]];
    }

    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
        PrintTopLevelUsage(Console.Out);
        return args.Length == 0 ? 2 : 0;
    }

    if (args[0] is "-v" or "--version")
    {
        PrintVersion(Console.Out);
        return 0;
    }

    var verb = args[0];
    var verbArgs = args.Skip(1).ToArray();

    switch (verb)
    {
        case "validate-dataset":
            return await RunValidateDatasetAsync(verbArgs, configPath);
        case "author-dataset":
            return await RunAuthorDatasetAsync(verbArgs, configPath);
        case "run":
            return await RunBenchAsync(verbArgs, configPath);
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
catch (FileNotFoundException ex)
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

static async Task<int> RunAuthorDatasetAsync(string[] verbArgs, string? configPath)
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

    return await BenchHost.RunAsync(configPath, async (engine, minervaSearhOptions, cancellationToken) => await AuthorDatasetRunner.RunAsync(
        engine, parsed.DatasetPath, parsed.Collection, Console.Out, cancellationToken));
}

static async Task<int> RunBenchAsync(string[] verbArgs, string? configPath)
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

    return await BenchHost.RunAsync(configPath, async (engine, minervaSearhOptions, cancellationToken) => await SweepDatasetRunner.RunAsync(
        engine, parsed.SweepPath, parsed.OutputDir, minervaSearhOptions, Console.Out, cancellationToken));
}

static async Task<int> RunValidateDatasetAsync(string[] verbArgs, string? configPath)
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

    return await BenchHost.RunAsync(configPath, async (engine, minervaSearhOptions, cancellationToken) => await DatasetValidationRunner.RunAsync(
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
          minerva-bench <verb> [verb-specific args] [--config PATH]

        --config PATH   Config file. Default: ~/.config/minerva/minerva-bench.json.
                        With DOTNET_ENVIRONMENT=ENV, <name>.ENV.json next to it is layered on top.

        Verbs:
          validate-dataset    Validate a JSONL eval dataset against an indexed collection.
          author-dataset      Builds a JSONL eval dataset
          run                 Run a sweep and write run.json, metrics.csv, details.jsonl

        Run `minerva-bench <verb> --help` for verb-specific help.

        Exit codes: 0 = success, 2 = bad args / validation failure, 1 = unhandled error.
        """);
}
