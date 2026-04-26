using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Configuration;
using Minerva.Readiness.Checks;
using Npgsql;

namespace Minerva.Tests.Readiness.Checks;

[Trait("Category", "Readiness")]
public class PgVectorExtensionCheckTests
{
    private const string Conn = "Host=db;Username=u;Password=p;Database=d";

    [Fact]
    public async Task ShortCircuits_WhenConnectionStringNull()
    {
        var check = Build(connectionString: null, probe: null);
        var result = await check.RunAsync(CancellationToken.None);
        Assert.True(result.Passed);
        Assert.Equal("MINERVA.PGVECTOR.NOT_CONFIGURED", result.Code);
    }

    [Fact]
    public async Task ShortCircuits_WhenDataSourceMissing()
    {
        var check = Build(Conn, probe: null);
        var result = await check.RunAsync(CancellationToken.None);
        Assert.True(result.Passed);
        Assert.Equal("MINERVA.PGVECTOR.NOT_CONFIGURED", result.Code);
    }

    [Fact]
    public async Task Passes_WhenExtensionEnabled()
    {
        var check = Build(Conn, new FakeProbe { Enabled = true, Available = true });

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.PGVECTOR.OK", result.Code);
    }

    [Fact]
    public async Task Fails_WhenAvailableButNotEnabled()
    {
        var check = Build(Conn, new FakeProbe { Enabled = false, Available = true });

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.PGVECTOR.NOT_ENABLED", result.Code);
        Assert.Contains("CREATE EXTENSION vector", result.Remediation);
    }

    [Fact]
    public async Task Fails_WhenNotAvailable()
    {
        var check = Build(Conn, new FakeProbe { Enabled = false, Available = false });

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.PGVECTOR.NOT_AVAILABLE", result.Code);
        Assert.Contains("not installed", result.Remediation);
    }

    [Fact]
    public async Task Fails_WhenProbeThrowsNpgsqlException()
    {
        var probe = new FakeProbe
        {
            EnabledThrows = new NpgsqlException("network unreachable Password=hunter2"),
        };
        var check = Build(Conn, probe);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.PGVECTOR.UNREACHABLE", result.Code);
        Assert.NotNull(result.Message);
        Assert.DoesNotContain("hunter2", result.Message);
    }

    private static PgVectorExtensionCheck Build(string? connectionString, IPgVectorExtensionProbe? probe)
    {
        return new PgVectorExtensionCheck(
            new MinervaOptions { ConnectionString = connectionString },
            probe,
            NullLogger<PgVectorExtensionCheck>.Instance);
    }

    private sealed class FakeProbe : IPgVectorExtensionProbe
    {
        public bool Enabled { get; init; }
        public bool Available { get; init; }
        public Exception? EnabledThrows { get; init; }
        public Exception? AvailableThrows { get; init; }

        public Task<bool> IsEnabledAsync(CancellationToken ct) =>
            EnabledThrows is not null ? Task.FromException<bool>(EnabledThrows) : Task.FromResult(Enabled);

        public Task<bool> IsAvailableAsync(CancellationToken ct) =>
            AvailableThrows is not null ? Task.FromException<bool>(AvailableThrows) : Task.FromResult(Available);
    }
}
