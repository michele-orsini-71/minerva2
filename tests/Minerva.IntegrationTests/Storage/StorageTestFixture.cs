using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Storage;
using Npgsql;

namespace Minerva.IntegrationTests.Storage;

public class StorageTestFixture : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Database=minerva_test;Username=postgres;Password=postgres";
    private const string DisableCleanupEnvVar = "MINERVA_TEST_DISABLE_CLEANUP";

    public NpgsqlDataSource DataSource { get; private set; } = null!;
    public SchemaInitializer SchemaInitializer { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("MINERVA_TEST_CONNSTRING")
            ?? DefaultConnectionString;

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        DataSource = dataSourceBuilder.Build();

        SchemaInitializer = new SchemaInitializer(DataSource,
            NullLogger<SchemaInitializer>.Instance);
        await SchemaInitializer.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
    }

    public async Task CleanupAsync(bool honorDisableFlag = true)
    {
        if (honorDisableFlag && IsCleanupDisabled())
            return;

        await using var conn = await DataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM chunks; DELETE FROM collections;", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static bool IsCleanupDisabled()
    {
        var value = Environment.GetEnvironmentVariable(DisableCleanupEnvVar);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

[CollectionDefinition("Storage")]
public class StorageCollection : ICollectionFixture<StorageTestFixture>;
