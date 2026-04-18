using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Minerva.Watcher.DI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMinervaWatcher(
        this IServiceCollection services,
        Action<WatcherOptions> configure)
    {
        services.Configure(configure);
        services.TryAddSingleton<MarkdownScanner>();
        services.AddHostedService<MarkdownSyncService>();
        return services;
    }
}
