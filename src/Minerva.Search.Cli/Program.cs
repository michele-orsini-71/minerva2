using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minerva;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Search.Cli;

try
{
    var parsed = SearchCliArgs.Parse(args, Console.Error);
    if (parsed is null)
    {
        SearchCliArgs.PrintUsage(Console.Error);
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

    var engine = await MinervaSearchBuilder.CreateAsync(
        config, loggerFactory, cts.Token);

    var overrides = new SearchOverrides
    {
        TopK = parsed.TopK,
        HybridAlpha = parsed.Alpha,
        CandidatePoolMultiplier = parsed.CandidatePoolMultiplier,
        ExpandContext = parsed.ExpandContext,
    };

    var results = await engine.SearchAsync(
        parsed.Query, parsed.Collections, overrides, cts.Token);

    ResultFormatter.Print(results, parsed, Console.Out);

    return results.Count == 0 ? 3 : 0;
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
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unhandled exception: {ex}");
    return 1;
}
