using Microsoft.Extensions.Logging;
using Minerva.DI;
using Minerva.Readiness;
using Minerva.Storage;
using Minerva.Tests.Readiness;
using Npgsql;

namespace Minerva.Tests.DI;

[Trait("Category", "Readiness")]
public class MinervaStartupServiceWarningTests
{
    [Fact]
    public void StartAsync_MarkerNotProbed_LogsWarning()
    {
        var logger = new RecordingLogger<MinervaStartupService>();
        var marker = new ReadinessProbeMarker();
        var service = new MinervaStartupService(BuildOfflineSchemaInitializer(), marker, logger);

        _ = SafeStart(service);

        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void StartAsync_MarkerProbed_DoesNotLogWarning()
    {
        var logger = new RecordingLogger<MinervaStartupService>();
        var marker = new ReadinessProbeMarker();
        marker.MarkProbed();
        var service = new MinervaStartupService(BuildOfflineSchemaInitializer(), marker, logger);

        _ = SafeStart(service);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void StartAsync_MarkerNotRegistered_DoesNotLogWarning()
    {
        var logger = new RecordingLogger<MinervaStartupService>();
        var service = new MinervaStartupService(BuildOfflineSchemaInitializer(), marker: null, logger: logger);

        _ = SafeStart(service);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    private static SchemaInitializer BuildOfflineSchemaInitializer()
    {
        // We never await the returned Task, so the data source is never opened.
        // It exists purely to satisfy the constructor.
        var dataSource = new NpgsqlDataSourceBuilder("Host=localhost;Port=1").Build();
        return new SchemaInitializer(dataSource, new RecordingLogger<SchemaInitializer>());
    }

    private static Task SafeStart(MinervaStartupService service)
    {
        // The warning fires synchronously inside StartAsync before the schema
        // initializer's task is returned. We deliberately do not await the
        // returned Task — there is no live Postgres in unit tests.
        Task task;
        try
        {
            task = service.StartAsync(CancellationToken.None);
        }
        catch
        {
            return Task.CompletedTask;
        }
        return task.ContinueWith(_ => { }, TaskScheduler.Default);
    }
}
