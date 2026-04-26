using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minerva.Configuration;
using Minerva.IntegrationTests.Storage;
using Minerva.Readiness.Checks;

namespace Minerva.IntegrationTests.Readiness;

[Collection("Storage")]
[Trait("Category", "Readiness")]
public class PostgresConnectivityCheckIntegrationTests
{
    private readonly StorageTestFixture _fixture;

    public PostgresConnectivityCheckIntegrationTests(StorageTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Passes_AgainstLiveDatabase()
    {
        var connectionString = Environment.GetEnvironmentVariable("MINERVA_TEST_CONNSTRING")
            ?? throw new InvalidOperationException("MINERVA_TEST_CONNSTRING not set");
        var services = new ServiceCollection();
        services.AddSingleton(_fixture.DataSource);
        var sp = services.BuildServiceProvider();

        var check = new PostgresConnectivityCheck(
            Options.Create(new MinervaOptions { ConnectionString = connectionString }),
            sp,
            NullLogger<PostgresConnectivityCheck>.Instance);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed, $"Expected Passed=true, got Code={result.Code}, Message={result.Message}");
        Assert.Equal("MINERVA.POSTGRES.OK", result.Code);
    }

    [Fact]
    public async Task Fails_WhenServerUnreachable()
    {
        // Point at a dead port — Npgsql will surface a connection failure (NpgsqlException).
        var deadConnectionString = "Host=localhost;Port=1;Username=u;Password=p;Database=d;Timeout=2;Command Timeout=2";
        var services = new ServiceCollection();
        services.AddSingleton(Npgsql.NpgsqlDataSource.Create(deadConnectionString));
        var sp = services.BuildServiceProvider();

        var check = new PostgresConnectivityCheck(
            Options.Create(new MinervaOptions { ConnectionString = deadConnectionString }),
            sp,
            NullLogger<PostgresConnectivityCheck>.Instance);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.POSTGRES.UNREACHABLE", result.Code);
        Assert.Contains("localhost:1", result.Remediation);
    }
}
