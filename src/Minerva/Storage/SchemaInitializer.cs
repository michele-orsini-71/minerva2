using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Minerva.Storage;

public partial class SchemaInitializer : ICollectionProvisioner
{
    private const long AdvisoryLockId = 0x4D494E455256_01; // "MINERV" + 01
    private static readonly Regex SafeCollectionNamePattern = SafeCollectionNameRegex();
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<SchemaInitializer> _logger;

    public SchemaInitializer(NpgsqlDataSource dataSource, ILogger<SchemaInitializer> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Advisory lock to prevent concurrent migration runs
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(@id)", conn))
        {
            lockCmd.Parameters.AddWithValue("id", AdvisoryLockId);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await EnsureMigrationsTableAsync(conn, ct);

            var applied = await GetAppliedMigrationsAsync(conn, ct);
            var migrations = LoadEmbeddedMigrations();

            foreach (var (name, sql) in migrations)
            {
                if (applied.Contains(name))
                {
                    _logger.LogDebug("Migration {Name} already applied, skipping", name);
                    continue;
                }

                _logger.LogInformation("Applying migration {Name}", name);
                await using var tx = await conn.BeginTransactionAsync(ct);

                await using (var cmd = new NpgsqlCommand(sql, conn, tx))
                {
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await using (var cmd = new NpgsqlCommand(
                    "INSERT INTO _migrations (name, applied_at) VALUES (@name, NOW())", conn, tx))
                {
                    cmd.Parameters.AddWithValue("name", name);
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
                _logger.LogInformation("Migration {Name} applied successfully", name);
            }
        }
        finally
        {
            await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@id)", conn);
            unlockCmd.Parameters.AddWithValue("id", AdvisoryLockId);
            await unlockCmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task EnsureHnswIndexAsync(string collectionName, int dimension,
        CancellationToken ct = default)
    {
        // Defense-in-depth: callers should validate, but re-check here because
        // collectionName and dimension are interpolated into DDL (identifiers
        // and typmod can't be parameterized).
        if (!SafeCollectionNamePattern.IsMatch(collectionName))
            throw new ArgumentException(
                $"Collection name '{collectionName}' contains unsafe characters for DDL.",
                nameof(collectionName));
        if (dimension <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimension),
                "Embedding dimension must be positive.");

        var indexName = $"idx_chunks_embedding_{collectionName.Replace("-", "_")}";
        var sql = $"""
            CREATE INDEX IF NOT EXISTS "{indexName}"
            ON chunks USING hnsw ((embedding::vector({dimension})) vector_cosine_ops)
            WHERE collection_name = @collection_name
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("collection_name", collectionName);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Ensured HNSW index {IndexName} for collection {Collection} (dim={Dimension})",
            indexName, collectionName, dimension);
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9-]*$")]
    private static partial Regex SafeCollectionNameRegex();

    private static async Task EnsureMigrationsTableAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS _migrations (
                name TEXT PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<HashSet<string>> GetAppliedMigrationsAsync(
        NpgsqlConnection conn, CancellationToken ct)
    {
        var result = new HashSet<string>();
        await using var cmd = new NpgsqlCommand("SELECT name FROM _migrations", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    private static List<(string Name, string Sql)> LoadEmbeddedMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = "Minerva.Storage.Migrations.";
        var resources = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var migrations = new List<(string, string)>();
        foreach (var resource in resources)
        {
            var name = resource[prefix.Length..]; // e.g. "001_initial.sql"
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException(
                    $"Embedded migration resource '{resource}' not found.");
            using var reader = new StreamReader(stream);
            migrations.Add((name, reader.ReadToEnd()));
        }

        return migrations;
    }
}
