using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minerva.DI;
using Minerva.MarkdownWatcher.DI;
using Minerva.Readiness;

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddMinerva(options =>
        builder.Configuration.GetSection("Minerva").Bind(options));

    builder.Services.AddMinervaWatcher(options =>
        builder.Configuration.GetSection("Watcher").Bind(options));

    var host = builder.Build();

    using (var scope = host.Services.CreateScope())
    {
        var checker = scope.ServiceProvider.GetRequiredService<IReadinessChecker>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var report = await checker.CheckReadinessAsync();

        ReadinessReportFormatter.LogReport(logger, report);

        if (!report.IsReady)
        {
            return 2;
        }
    }

    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unhandled exception during startup: {ex}");
    return 1;
}
