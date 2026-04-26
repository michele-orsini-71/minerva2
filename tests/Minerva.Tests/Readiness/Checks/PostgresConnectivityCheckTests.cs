using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Configuration;
using Minerva.Readiness.Checks;
using Npgsql;

namespace Minerva.Tests.Readiness.Checks;

[Trait("Category", "Readiness")]
public class PostgresConnectivityCheckTests
{
    private const string Conn = "Host=db.example;Port=5432;Username=u;Password=hunter2;Database=mydb";

    [Fact]
    public async Task ShortCircuits_WhenConnectionStringNull()
    {
        var check = Build(connectionString: null, probe: null);
        var result = await check.RunAsync(CancellationToken.None);
        Assert.True(result.Passed);
        Assert.Equal("MINERVA.POSTGRES.NOT_CONFIGURED", result.Code);
    }

    [Fact]
    public async Task ShortCircuits_WhenDataSourceMissing()
    {
        var check = Build(Conn, probe: null);
        var result = await check.RunAsync(CancellationToken.None);
        Assert.True(result.Passed);
        Assert.Equal("MINERVA.POSTGRES.NOT_CONFIGURED", result.Code);
    }

    [Fact]
    public async Task Passes_WhenProbeSucceeds()
    {
        var check = Build(Conn, new FakeProbe(_ => Task.CompletedTask));
        var result = await check.RunAsync(CancellationToken.None);
        Assert.True(result.Passed);
        Assert.Equal("MINERVA.POSTGRES.OK", result.Code);
    }

    [Fact]
    public async Task DatabaseMissing_TranslatesToCode_3D000()
    {
        var ex = NewPostgresException("3D000", "database \"mydb\" does not exist");
        var check = Build(Conn, new FakeProbe(_ => throw ex));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.POSTGRES.DATABASE_MISSING", result.Code);
        Assert.Contains("CREATE DATABASE \"mydb\"", result.Remediation);
    }

    [Fact]
    public async Task AuthFailed_TranslatesToCode_28P01()
    {
        var ex = NewPostgresException("28P01", "password authentication failed");
        var check = Build(Conn, new FakeProbe(_ => throw ex));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.POSTGRES.AUTH_FAILED", result.Code);
    }

    [Fact]
    public async Task NetworkFailure_TranslatesToUnreachable()
    {
        var ex = new NpgsqlException("connection refused");
        var check = Build(Conn, new FakeProbe(_ => throw ex));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.POSTGRES.UNREACHABLE", result.Code);
        Assert.Contains("db.example:5432", result.Remediation);
    }

    [Fact]
    public async Task FailureMessage_RedactsPassword()
    {
        var ex = new NpgsqlException(
            "Failed connecting to Host=db;Password=hunter2;Database=mydb");
        var check = Build(Conn, new FakeProbe(_ => throw ex));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.NotNull(result.Message);
        Assert.DoesNotContain("hunter2", result.Message);
    }

    private static PostgresConnectivityCheck Build(string? connectionString, IPostgresConnectivityProbe? probe)
    {
        return new PostgresConnectivityCheck(
            new MinervaOptions { ConnectionString = connectionString },
            probe,
            NullLogger<PostgresConnectivityCheck>.Instance);
    }

    private static PostgresException NewPostgresException(string sqlState, string message) =>
        new(messageText: message, severity: "ERROR", invariantSeverity: "ERROR", sqlState: sqlState);

    private sealed class FakeProbe : IPostgresConnectivityProbe
    {
        private readonly Func<CancellationToken, Task> _open;
        public FakeProbe(Func<CancellationToken, Task> open) => _open = open;
        public Task OpenAsync(CancellationToken ct) => _open(ct);
    }
}
