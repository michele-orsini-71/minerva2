using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minerva;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Search.Cli;
using Minerva.Utils;

try
{
    var parsed = SearchCliArgs.Parse(args, Console.Error);
    if (parsed is null)
    {
        SearchCliArgs.PrintUsage(Console.Error);
        return 2;
    } 

    if (parsed.version)
    {
        SearchCliArgs.PrintVersion(Console.Out);
        return 0;
    } 

    var config = new ConfigurationBuilder()
        .AddMinervaConfigFiles(parsed.ConfigPath)
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
        CandidatePoolSize = parsed.CandidatePoolSize,
        ExpandContext = parsed.ExpandContext,
        EnableReranker = parsed.EnableReranker,
        RerankDepth = parsed.RerankDepth,
        CascadeDepth = parsed.CascadeDepth
    };

    var results = await engine.SearchAsync(
        parsed.Query, parsed.Collection, overrides, cts.Token);

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
