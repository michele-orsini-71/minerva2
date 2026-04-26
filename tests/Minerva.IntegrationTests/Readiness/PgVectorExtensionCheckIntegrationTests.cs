using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minerva.Configuration;
using Minerva.IntegrationTests.Storage;
using Minerva.Readiness.Checks;

namespace Minerva.IntegrationTests.Readiness;

[Collection("Storage")]
[Trait("Category", "Readiness")]
public class PgVectorExtensionCheckIntegrationTests
{
    private readonly StorageTestFixture _fixture;

    public PgVectorExtensionCheckIntegrationTests(StorageTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Passes_WhenExtensionEnabled()
    {
        // StorageTestFixture.SchemaInitializer.InitializeAsync() runs CREATE EXTENSION IF NOT EXISTS vector
        // during fixture init, so the live test DB always has pgvector enabled.
        var connectionString = Environment.GetEnvironmentVariable("MINERVA_TEST_CONNSTRING")
            ?? throw new InvalidOperationException("MINERVA_TEST_CONNSTRING not set");
        var services = new ServiceCollection();
        services.AddSingleton(_fixture.DataSource);
        var sp = services.BuildServiceProvider();

        var check = new PgVectorExtensionCheck(
            Options.Create(new MinervaOptions { ConnectionString = connectionString }),
            sp,
            NullLogger<PgVectorExtensionCheck>.Instance);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed, $"Expected Passed=true, got Code={result.Code}, Message={result.Message}");
        Assert.Equal("MINERVA.PGVECTOR.OK", result.Code);
    }
}
