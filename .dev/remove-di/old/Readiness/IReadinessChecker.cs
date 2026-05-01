namespace Minerva.Readiness;

public interface IReadinessChecker
{
    Task<ReadinessReport> CheckReadinessAsync(CancellationToken ct = default);
}
