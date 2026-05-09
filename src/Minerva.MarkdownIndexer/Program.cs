using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minerva;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.MarkdownIndexer;

try
{
    var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{env}.json", optional: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args)
        .Build();

    var minervaOptions = MinervaOptionsBinder.Bind(config.GetSection("Minerva"));
    var indexerOptions = IndexerOptionsBinder.Bind(config.GetSection("Indexer"));

    var forceRecreate = args.Contains("--force-recreate");

    using var loggerFactory = LoggerFactory.Create(b =>
    {
        b.AddConfiguration(config.GetSection("Logging"));
        b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
    });
    var programLogger = loggerFactory.CreateLogger("Minerva.MarkdownIndexer");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        programLogger.LogInformation("Cancellation requested, shutting down…");
        cts.Cancel();
    };

    var engine = await MinervaBuilder.CreateAsync(minervaOptions, loggerFactory, cts.Token);
    var indexer = await MarkdownIndexerBuilder.CreateAsync(
        indexerOptions, engine, loggerFactory, forceRecreate, cts.Token);

    await indexer.RunAsync(cts.Token);
    return 0;
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
