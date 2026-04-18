using Microsoft.Extensions.Hosting;
using Minerva.Storage;

namespace Minerva.DI;

public class MinervaStartupService : IHostedService
{
    private readonly SchemaInitializer _schemaInitializer;

    public MinervaStartupService(SchemaInitializer schemaInitializer)
    {
        _schemaInitializer = schemaInitializer;
    }

    public Task StartAsync(CancellationToken ct) =>
        _schemaInitializer.InitializeAsync(ct);

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
