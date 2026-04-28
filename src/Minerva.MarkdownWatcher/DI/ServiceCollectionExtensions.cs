using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minerva.MarkdownWatcher.Readiness;
using Minerva.Readiness;

namespace Minerva.MarkdownWatcher.DI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMinervaWatcher(
        this IServiceCollection services,
        Action<WatcherOptions> configure)
    {
        services.Configure(configure);
        services.TryAddSingleton<IMarkdownScanner, MarkdownScanner>();
        services.TryAddSingleton(sp => new MarkdownIngestionHandler(
            sp.GetRequiredService<IMinervaEngine>(),
            sp.GetRequiredService<IMarkdownScanner>(),
            sp.GetRequiredService<IOptions<WatcherOptions>>().Value.CollectionName,
            sp.GetRequiredService<ILogger<MarkdownIngestionHandler>>()));
        services.AddHostedService<MarkdownSyncService>();

        services.AddMinervaReadinessCheck<RootPathExistsCheck>();
        services.AddMinervaReadinessCheck<CollectionNameValidCheck>();
        services.AddMinervaReadinessCheck<CollectionDimensionMatchCheck>();

        return services;
    }
}
