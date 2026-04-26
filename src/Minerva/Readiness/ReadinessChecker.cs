using Microsoft.Extensions.Logging;

namespace Minerva.Readiness;

public sealed class ReadinessChecker : IReadinessChecker
{
    private static readonly TimeSpan DefaultPerCheckTimeout = TimeSpan.FromSeconds(30);

    private readonly IEnumerable<IReadinessCheck> _checks;
    private readonly IReadinessProbeMarker _marker;
    private readonly ILogger<ReadinessChecker> _logger;

    public ReadinessChecker(
        IEnumerable<IReadinessCheck> checks,
        IReadinessProbeMarker marker,
        ILogger<ReadinessChecker> logger)
    {
        _checks = checks;
        _marker = marker;
        _logger = logger;
    }

    public async Task<ReadinessReport> CheckReadinessAsync(CancellationToken ct = default)
    {
        var results = new List<ReadinessCheckResult>();

        foreach (var check in _checks)
        {
            var timeout = (check as IReadinessCheckTimeout)?.Timeout ?? DefaultPerCheckTimeout;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);

            try
            {
                results.Add(await check.RunAsync(linked.Token));
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                results.Add(new ReadinessCheckResult(
                    check.Name,
                    check.Category,
                    Passed: false,
                    Code: "MINERVA.READINESS.TIMEOUT",
                    Message: $"Check '{check.Name}' did not complete within {timeout.TotalSeconds:0}s.",
                    Remediation: "Investigate why this prerequisite is slow; if a local model is cold-loading, give it a warmup or extend the timeout."));
            }
            catch (Exception ex)
            {
                results.Add(new ReadinessCheckResult(
                    check.Name,
                    check.Category,
                    Passed: false,
                    Code: "MINERVA.READINESS.UNHANDLED",
                    Message: Redact.Apply(ex.Message),
                    Remediation: "Unexpected check failure — see logs for the full exception."));
                _logger.LogDebug(ex, "Readiness check '{Name}' threw unexpectedly", check.Name);
            }
        }

        _marker.MarkProbed();
        return new ReadinessReport(results);
    }
}
