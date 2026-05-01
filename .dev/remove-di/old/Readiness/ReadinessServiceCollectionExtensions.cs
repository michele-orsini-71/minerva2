using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Minerva.Readiness;

public static class ReadinessServiceCollectionExtensions
{
    public static IServiceCollection AddMinervaReadinessCheck<T>(this IServiceCollection services)
        where T : class, IReadinessCheck
    {
        AddMinervaReadinessCore(services);
        services.AddTransient<IReadinessCheck, T>();
        return services;
    }

    internal static IServiceCollection AddMinervaReadinessCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IReadinessProbeMarker, ReadinessProbeMarker>();
        services.TryAddSingleton<IReadinessChecker, ReadinessChecker>();
        return services;
    }
}
