using Minerva.Storage;
using Npgsql;

namespace Minerva.IntegrationTests.Storage;

[Collection("Storage")]
[Trait("Category", "Storage")]
public class DatabasePreflightTests
{
    private readonly StorageTestFixture _fixture;

    public DatabasePreflightTests(StorageTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Preflight_OnProvisionedDatabase_ReportsNoFailures()
    {
        var preflight = new DatabasePreflight(_fixture.DataSource);

        var failures = await preflight.PreflightAsync();

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Preflight_WhenDatabaseDoesNotExist_ReportsSinglePostgresFailure()
    {
        var builder = new NpgsqlConnectionStringBuilder(_fixture.DataSource.ConnectionString)
        {
            Database = "minerva_test_does_not_exist",
        };
        await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        var preflight = new DatabasePreflight(dataSource);

        var failures = await preflight.PreflightAsync();

        var failure = Assert.Single(failures);
        Assert.Equal("Storage.Postgres", failure.Stage);
        Assert.Contains("does not exist", failure.Reason);
    }

    [Fact]
    public async Task Preflight_OnDatabaseWithoutExtensions_ReportsBothExtensionsNotEnabled()
    {
        // The fixture connects as a superuser, so a scratch database is cheap to create.
        // template0 guarantees no extension is inherited from template1.
        const string bareDatabase = "minerva_test_preflight_bare";
        await ExecuteAsync(_fixture.DataSource,
            $"DROP DATABASE IF EXISTS {bareDatabase} WITH (FORCE); CREATE DATABASE {bareDatabase} TEMPLATE template0");
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(_fixture.DataSource.ConnectionString)
            {
                Database = bareDatabase,
            };
            await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
            var preflight = new DatabasePreflight(dataSource);

            var failures = await preflight.PreflightAsync();

            Assert.Equal(2, failures.Count);
            var vector = Assert.Single(failures, f => f.Stage == "Storage.PgVector");
            var search = Assert.Single(failures, f => f.Stage == "Storage.PgSearch");
            Assert.Contains("not enabled in this database", vector.Reason);
            Assert.Contains("not enabled in this database", search.Reason);
        }
        finally
        {
            await ExecuteAsync(_fixture.DataSource, $"DROP DATABASE IF EXISTS {bareDatabase} WITH (FORCE)");
        }
    }

    private static async Task ExecuteAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
