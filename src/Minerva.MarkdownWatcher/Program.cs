using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Minerva.DI;
using Minerva.MarkdownWatcher.DI;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMinerva(options =>
    builder.Configuration.GetSection("Minerva").Bind(options));

builder.Services.AddMinervaWatcher(options =>
    builder.Configuration.GetSection("Watcher").Bind(options));

await builder.Build().RunAsync();
