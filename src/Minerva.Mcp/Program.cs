using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minerva;
using ModelContextProtocol;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory,
});


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