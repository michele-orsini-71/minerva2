using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Readiness;
using Minerva.Storage;

namespace Minerva.DI;

public class MinervaStartupService : IHostedService
{
    private readonly SchemaInitializer _schemaInitializer;
    private readonly IReadinessProbeMarker? _marker;
    private readonly ILogger<MinervaStartupService> _logger;

    public MinervaStartupService(
        SchemaInitializer schemaInitializer,
        IReadinessProbeMarker? marker = null,
        ILogger<MinervaStartupService>? logger = null)
    {
        _schemaInitializer = schemaInitializer;
        _marker = marker;
        _logger = logger ?? NullLogger<MinervaStartupService>.Instance;
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (_marker is { Probed: false })
        {
            _logger.LogWarning(
                "Minerva preflight was not invoked before host start; failures will surface as runtime exceptions.");
        }

        return _schemaInitializer.InitializeAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
