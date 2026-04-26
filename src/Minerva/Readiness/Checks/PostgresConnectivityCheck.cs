using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minerva.Configuration;
using Npgsql;

namespace Minerva.Readiness.Checks;

internal interface IPostgresConnectivityProbe
{
    Task OpenAsync(CancellationToken ct);
}

public sealed class PostgresConnectivityCheck : IReadinessCheck, IReadinessCheckTimeout
{
    private readonly MinervaOptions _options;
    private readonly IPostgresConnectivityProbe? _probe;
    private readonly ILogger<PostgresConnectivityCheck> _logger;

    public PostgresConnectivityCheck(
        IOptions<MinervaOptions> options,
        IServiceProvider serviceProvider,
        ILogger<PostgresConnectivityCheck> logger)
    {
        _options = options.Value;
        var ds = serviceProvider.GetService<NpgsqlDataSource>();
        _probe = ds is null ? null : new NpgsqlConnectivityProbe(ds);
        _logger = logger;
    }

    internal PostgresConnectivityCheck(
        MinervaOptions options,
        IPostgresConnectivityProbe? probe,
        ILogger<PostgresConnectivityCheck> logger)
    {
        _options = options;
        _probe = probe;
        _logger = logger;
    }

    public string Name => nameof(PostgresConnectivityCheck);
    public ReadinessCategory Category => ReadinessCategory.Storage;
    public TimeSpan Timeout => TimeSpan.FromSeconds(5);

    public async Task<ReadinessCheckResult> RunAsync(CancellationToken ct)
    {
        if (_options.ConnectionString is null || _probe is null)
        {
            return new ReadinessCheckResult(
                Name, Category, Passed: true,
                Code: "MINERVA.POSTGRES.NOT_CONFIGURED",
                Message: null,
                Remediation: null);
        }

        try
        {
            await _probe.OpenAsync(ct);
            return new ReadinessCheckResult(
                Name, Category, Passed: true,
                Code: "MINERVA.POSTGRES.OK",
                Message: null,
                Remediation: null);
        }
        catch (PostgresException ex) when (ex.SqlState == "3D000")
        {
            _logger.LogDebug(ex, "Postgres database missing");
            var dbName = TryGetDatabase(_options.ConnectionString) ?? "<unknown>";
            return new ReadinessCheckResult(
                Name, Category, Passed: false,
                Code: "MINERVA.POSTGRES.DATABASE_MISSING",
                Message: Redact.Apply(ex.Message),
                Remediation: $"Database '{dbName}' does not exist. Run: CREATE DATABASE \"{dbName}\";");
        }
        catch (PostgresException ex) when (ex.SqlState == "28P01")
        {
            _logger.LogDebug(ex, "Postgres authentication failed");
            return new ReadinessCheckResult(
                Name, Category, Passed: false,
                Code: "MINERVA.POSTGRES.AUTH_FAILED",
                Message: Redact.Apply(ex.Message),
                Remediation: "Postgres rejected the credentials. Verify the username and password in Minerva:ConnectionString.");
        }
        catch (Exception ex) when (ex is NpgsqlException || ex is TimeoutException)
        {
            _logger.LogDebug(ex, "Postgres unreachable");
            var (host, port) = TryGetHostPort(_options.ConnectionString);
            return new ReadinessCheckResult(
                Name, Category, Passed: false,
                Code: "MINERVA.POSTGRES.UNREACHABLE",
                Message: Redact.Apply(ex.Message),
                Remediation: $"Postgres at '{host}:{port}' did not respond. Verify the server is running and reachable from this host.");
        }
    }

    private static string? TryGetDatabase(string connectionString)
    {
        try { return new NpgsqlConnectionStringBuilder(connectionString).Database; }
        catch { return null; }
    }

    private static (string Host, int Port) TryGetHostPort(string connectionString)
    {
        try
        {
            var b = new NpgsqlConnectionStringBuilder(connectionString);
            return (b.Host ?? "<unknown>", b.Port);
        }
        catch
        {
            return ("<unknown>", 0);
        }
    }

    private sealed class NpgsqlConnectivityProbe : IPostgresConnectivityProbe
    {
        private readonly NpgsqlDataSource _dataSource;
        public NpgsqlConnectivityProbe(NpgsqlDataSource dataSource) => _dataSource = dataSource;

        public async Task OpenAsync(CancellationToken ct)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
        }
    }
}
