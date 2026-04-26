using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minerva.Configuration;
using Npgsql;

namespace Minerva.Readiness.Checks;

public sealed class ConnectionStringParseCheck : IReadinessCheck, IReadinessCheckTimeout
{
    private readonly MinervaOptions _options;
    private readonly ILogger<ConnectionStringParseCheck> _logger;

    public ConnectionStringParseCheck(
        IOptions<MinervaOptions> options,
        ILogger<ConnectionStringParseCheck> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => nameof(ConnectionStringParseCheck);
    public ReadinessCategory Category => ReadinessCategory.Configuration;
    public TimeSpan Timeout => TimeSpan.FromSeconds(2);

    public Task<ReadinessCheckResult> RunAsync(CancellationToken ct)
    {
        if (_options.ConnectionString is null)
        {
            return Task.FromResult(new ReadinessCheckResult(
                Name, Category, Passed: true,
                Code: "MINERVA.CONFIG.CONN_STRING_NOT_CONFIGURED",
                Message: null,
                Remediation: null));
        }

        try
        {
            _ = new NpgsqlConnectionStringBuilder(_options.ConnectionString);
            return Task.FromResult(new ReadinessCheckResult(
                Name, Category, Passed: true,
                Code: "MINERVA.CONFIG.CONN_STRING_OK",
                Message: null,
                Remediation: null));
        }
        catch (ArgumentException ex)
        {
            _logger.LogDebug(ex, "Connection string failed to parse");
            return Task.FromResult(new ReadinessCheckResult(
                Name, Category, Passed: false,
                Code: "MINERVA.CONFIG.CONN_STRING_INVALID",
                Message: Redact.Apply(ex.Message),
                Remediation: "Check the Minerva:ConnectionString format. Expected: 'Host=...;Database=...;Username=...;Password=...'."));
        }
    }
}
