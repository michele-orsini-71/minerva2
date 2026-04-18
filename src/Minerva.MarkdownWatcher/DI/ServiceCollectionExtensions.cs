using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Minerva.MarkdownWatcher.DI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMinervaWatcher(
        this IServiceCollection services,
        Action<WatcherOptions> configure)
    {
        services.Configure(configure);
        services.TryAddSingleton<MarkdownScanner>();
        services.TryAddSingleton<IMarkdownScanner>(
            sp => sp.GetRequiredService<MarkdownScanner>());
        services.TryAddSingleton(sp => new MarkdownIngestionHandler(
            sp.GetRequiredService<IMinervaEngine>(),
            sp.GetRequiredService<IMarkdownScanner>(),
            sp.GetRequiredService<IOptions<WatcherOptions>>().Value.CollectionName,
            sp.GetRequiredService<ILogger<MarkdownIngestionHandler>>()));
        services.AddHostedService<MarkdownSyncService>();
        return services;
    }
}
