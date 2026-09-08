using Minerva.Exceptions;
using Npgsql;

namespace Minerva.Storage;

public sealed class DatabasePreflight
{
    private readonly NpgsqlDataSource _dataSource;

    public DatabasePreflight(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<PreflightFailure?> PreflightAsync(CancellationToken ct = default)
    {
        NpgsqlConnection conn;
        try
        {
            conn = await _dataSource.OpenConnectionAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "3D000")
        {
            return new PreflightFailure(
                "Storage.Postgres",
                $"Database does not exist: {ex.MessageText}. Create it (CREATE DATABASE \"...\";) or fix Minerva:ConnectionString.",
                ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "28P01")
        {
            return new PreflightFailure(
                "Storage.Postgres",
                "Postgres rejected the credentials. Verify username and password in Minerva:ConnectionString.",
                ex);
        }
        catch (Exception ex) when (ex is NpgsqlException || ex is TimeoutException)
        {
            return new PreflightFailure(
                "Storage.Postgres",
                $"Postgres did not respond: {ex.Message}. Verify the server is running and reachable.",
                ex);
        }

        await using (conn)
        {
            return await CheckPgVectorAsync(conn, ct)
                ?? await CheckPgSearchBitmapIntersectionAsync(conn, ct);
        }
    }

    private static async Task<PreflightFailure?> CheckPgVectorAsync(
        NpgsqlConnection conn, CancellationToken ct)
    {
        await using (var cmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_extension WHERE extname = 'vector'", conn))
        {
            var enabled = await cmd.ExecuteScalarAsync(ct);
            if (enabled is not null && enabled is not DBNull)
                return null;
        }

        await using (var cmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_available_extensions WHERE name = 'vector'", conn))
        {
            var available = await cmd.ExecuteScalarAsync(ct);
            if (available is not null && available is not DBNull)
            {
                return new PreflightFailure(
                    "Storage.PgVector",
                    "pgvector is installed on the server but not enabled in this database. Run: CREATE EXTENSION vector; (may require superuser).");
            }
        }

        return new PreflightFailure(
            "Storage.PgVector",
            "pgvector is not visible to this connection. Install it via your distro (e.g. apt install postgresql-16-pgvector) or enable it via your managed-Postgres provider.");
    }

    // These releases fail with "bitmap intersection stream ... claimed twice" on Minerva's
    // filter + BM25 query shape. See sql-scripts/disable-pgsearch-bitmap-intersection.sql.
    private static readonly string[] PgSearchVersionsWithBitmapIntersectionBug = ["0.25.5", "0.25.6"];

    private static async Task<PreflightFailure?> CheckPgSearchBitmapIntersectionAsync(
        NpgsqlConnection conn, CancellationToken ct)
    {
        string? version;
        await using (var cmd = new NpgsqlCommand(
            "SELECT extversion FROM pg_extension WHERE extname = 'pg_search'", conn))
        {
            version = await cmd.ExecuteScalarAsync(ct) as string;
        }

        if (version is null || !PgSearchVersionsWithBitmapIntersectionBug.Contains(version))
            return null;

        await using (var cmd = new NpgsqlCommand(
            "SHOW paradedb.enable_bitmap_intersection", conn))
        {
            var setting = await cmd.ExecuteScalarAsync(ct) as string;
            if (string.Equals(setting, "off", StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return new PreflightFailure(
            "Storage.PgSearch",
            $"pg_search {version} has a bitmap intersection bug that breaks Minerva's BM25 queries. Run sql-scripts/disable-pgsearch-bitmap-intersection.sql as the database owner, then reconnect.");
    }
}
