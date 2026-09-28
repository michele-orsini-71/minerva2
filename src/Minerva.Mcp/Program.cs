using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minerva;
using Minerva.Utils;
using ModelContextProtocol;

if (args.Contains("-v") || args.Contains("--version"))
{
    VersionInfo.PrintVersion(Console.Out, Assembly.GetExecutingAssembly());
    return;
}

// The config path must be known before the real configuration is built.
var configPath = new ConfigurationBuilder().AddCommandLine(args).Build()["config"];

// Defaults off: they would read appsettings.json from the current directory,
// which MCP clients choose.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    DisableDefaults = true,
});
builder.Configuration
    .AddMinervaConfigFiles(configPath)
    .AddEnvironmentVariables()
    .AddCommandLine(args);


builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// removes default providers that do to stdout and interfere with MCP stdio output
builder.Logging.ClearProviders();

ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)) ;
var engine = await MinervaSearchBuilder.CreateAsync(builder.Configuration, loggerFactory, CancellationToken.None);

builder.Services.AddSingleton(loggerFactory);
builder.Services.AddSingleton(engine);

var app = builder.Build();

await app.RunAsync();