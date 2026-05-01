namespace Minerva.Readiness;

public record ReadinessReport(IReadOnlyList<ReadinessCheckResult> Results)
{
    public bool IsReady => Results.All(r => r.Passed);
}
