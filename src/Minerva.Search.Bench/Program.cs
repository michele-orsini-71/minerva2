using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minerva;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Search.Bench;
using Minerva.Search.Bench.Validation;

try
{
    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
        PrintTopLevelUsage(Console.Out);
        return args.Length == 0 ? 2 : 0;
    }

    var verb = args[0];
    var verbArgs = args.Skip(1).ToArray();

    switch (verb)
    {
        case "validate-dataset":
            return await RunValidateDatasetAsync(verbArgs);
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

    var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{env}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    using var loggerFactory = LoggerFactory.Create(b =>
    {
        b.AddConfiguration(config.GetSection("Logging"));
        b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
    });

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var engine = await MinervaSearchBuilder.CreateAsync(config, loggerFactory, cts.Token);

    return await DatasetValidationRunner.RunAsync(
        engine, parsed.DatasetPath, parsed.Collection, Console.Out, cts.Token);
}

static void PrintTopLevelUsage(TextWriter w)
{
    w.WriteLine("""
        Usage:
          minerva-bench <verb> [verb-specific args]

        Verbs:
          validate-dataset    Validate a JSONL eval dataset against an indexed collection.

        Run `minerva-bench <verb> --help` for verb-specific help.

        Exit codes: 0 = success, 2 = bad args / validation failure, 1 = unhandled error.
        """);
}
