using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minerva;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.MarkdownIndexer;
using NReco.Logging.File;

try
{
    if (args.Contains("-v") || args.Contains("--version"))
    {
        PrintVersion(Console.Out);
        return 0;
    }

    var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{env}.json", optional: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args)
        .Build();

    var indexerOptions = IndexerOptionsBinder.Bind(config.GetSection("Indexer"));

    var configuredLogPath = config["Logging:File:Path"];
    string? logFilePath = null;
    if (!string.IsNullOrWhiteSpace(configuredLogPath))
    {
        logFilePath = ResolveLogFilePath(configuredLogPath);
        try
        {
            EnsureLogFileWritable(logFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot write to log file '{logFilePath}': {ex.Message}");
            return 2;
        }
    }

    using var loggerFactory = LoggerFactory.Create(b =>
    {
        b.AddConfiguration(config.GetSection("Logging"));
        b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
        if (logFilePath is not null)
        {
            b.AddFile(logFilePath, opts =>
            {
                opts.Append = true;
                opts.FileSizeLimitBytes = 0;
                opts.MaxRollingFiles = 0;
            });
        }
    });
    var programLogger = loggerFactory.CreateLogger("Minerva.MarkdownIndexer");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        programLogger.LogInformation("Cancellation requested, shutting down…");
        cts.Cancel();
    };

    var engine = await MinervaIngestBuilder.CreateAsync(
        config.GetSection("Minerva"), loggerFactory, cts.Token);
    var indexer = await MarkdownIndexerBuilder.CreateAsync(
        indexerOptions, engine, loggerFactory, cts.Token);

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

static string ResolveLogFilePath(string configured)
{
    var raw = configured;
    if (raw.StartsWith("~/", StringComparison.Ordinal))
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        raw = Path.Combine(home, raw[2..]);
    }
    return raw.Replace("{Date}", DateTime.Now.ToString("yyyy-MM-dd"));
}

static void EnsureLogFileWritable(string path)
{
    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(dir))
    {
        Directory.CreateDirectory(dir);
    }
    using var probe = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
}

static void PrintVersion(TextWriter w)
{
    w.WriteLine($"{Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"}");
}
