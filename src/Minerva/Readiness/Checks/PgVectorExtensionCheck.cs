using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minerva.Configuration;
using Npgsql;

namespace Minerva.Readiness.Checks;

internal interface IPgVectorExtensionProbe
{
    Task<bool> IsEnabledAsync(CancellationToken ct);
    Task<bool> IsAvailableAsync(CancellationToken ct);
}

public sealed class PgVectorExtensionCheck : IReadinessCheck, IReadinessCheckTimeout
{
    private readonly MinervaOptions _options;
    private readonly IPgVectorExtensionProbe? _probe;
    private readonly ILogger<PgVectorExtensionCheck> _logger;

    public PgVectorExtensionCheck(
        IOptions<MinervaOptions> options,
        IServiceProvider serviceProvider,
        ILogger<PgVectorExtensionCheck> logger)
    {
        _options = options.Value;
        var ds = serviceProvider.GetService<NpgsqlDataSource>();
        _probe = ds is null ? null : new NpgsqlPgVectorProbe(ds);
        _logger = logger;
    }

    internal PgVectorExtensionCheck(
        MinervaOptions options,
        IPgVectorExtensionProbe? probe,
        ILogger<PgVectorExtensionCheck> logger)
    {
        _options = options;
        _probe = probe;
        _logger = logger;
    }

    public string Name => nameof(PgVectorExtensionCheck);
    public ReadinessCategory Category => ReadinessCategory.Storage;
    public TimeSpan Timeout => TimeSpan.FromSeconds(5);

    public async Task<ReadinessCheckResult> RunAsync(CancellationToken ct)
    {
        if (_options.ConnectionString is null || _probe is null)
        {
            return new ReadinessCheckResult(
                Name, Category, Passed: true,
                Code: "MINERVA.PGVECTOR.NOT_CONFIGURED",
                Message: null,
                Remediation: null);
        }

        try
        {
            if (await _probe.IsEnabledAsync(ct))
            {
                return new ReadinessCheckResult(
                    Name, Category, Passed: true,
                    Code: "MINERVA.PGVECTOR.OK",
                    Message: null,
                    Remediation: null);
            }

            if (await _probe.IsAvailableAsync(ct))
            {
                return new ReadinessCheckResult(
                    Name, Category, Passed: false,
                    Code: "MINERVA.PGVECTOR.NOT_ENABLED",
                    Message: "pgvector is installed on the server but not enabled in this database.",
                    Remediation: "pgvector is installed on the server but not enabled in this database. Run: CREATE EXTENSION vector; (may require superuser; on managed Postgres see your provider's docs for enabling pgvector).");
            }

            return new ReadinessCheckResult(
                Name, Category, Passed: false,
                Code: "MINERVA.PGVECTOR.NOT_AVAILABLE",
                Message: "pgvector is not visible to this connection.",
                Remediation: "pgvector is either not installed on the server, OR your role cannot see pg_available_extensions (Azure Database for PostgreSQL / RDS may hide this catalog from non-admin roles). Install pgvector via your distro (e.g. apt install postgresql-16-pgvector) or check your managed-Postgres provider's portal/CLI.");
        }
        catch (Exception ex) when (ex is NpgsqlException || ex is TimeoutException)
        {
            _logger.LogDebug(ex, "pgvector probe failed: Postgres unreachable");
            return new ReadinessCheckResult(
                Name, Category, Passed: false,
                Code: "MINERVA.PGVECTOR.UNREACHABLE",
                Message: Redact.Apply(ex.Message),
                Remediation: "Postgres is not reachable; see PostgresConnectivityCheck for diagnosis.");
        }
    }

    private sealed class NpgsqlPgVectorProbe : IPgVectorExtensionProbe
    {
        private readonly NpgsqlDataSource _dataSource;
        public NpgsqlPgVectorProbe(NpgsqlDataSource dataSource) => _dataSource = dataSource;

        public async Task<bool> IsEnabledAsync(CancellationToken ct) =>
            await ScalarExistsAsync("SELECT 1 FROM pg_extension WHERE extname = 'vector'", ct);

        public async Task<bool> IsAvailableAsync(CancellationToken ct) =>
            await ScalarExistsAsync("SELECT 1 FROM pg_available_extensions WHERE name = 'vector'", ct);

        private async Task<bool> ScalarExistsAsync(string sql, CancellationToken ct)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is not null && result is not DBNull;
        }
    }
}
