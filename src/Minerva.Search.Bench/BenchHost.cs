using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minerva;
using Minerva.Configuration;
using Minerva.Utils;

public static class BenchHost
{
    public delegate Task<int> BenchDelegate(ISearchEngine engine, MinervaSearchOptions options, CancellationToken cancellationToken);

    public static async Task<int> RunAsync(string? configPath, BenchDelegate bench)
    {
        var config = new ConfigurationBuilder()
            .AddMinervaConfigFiles(configPath)
            .AddEnvironmentVariables()
            .Build();

        using var loggerFactory = LoggerFactory.Create(b =>
        {
            b.AddConfiguration(config.GetSection("Logging"));
            b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
        });

        var minervaSearchOptions = MinervaSearchOptionsBinder.Bind(config);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var engine = await MinervaSearchBuilder.CreateAsync(config, loggerFactory, cts.Token);
        return await bench(engine, minervaSearchOptions, cts.Token);
    }
}