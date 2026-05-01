namespace Minerva.Readiness;

public interface IReadinessCheck
{
    string Name { get; }
    ReadinessCategory Category { get; }
    Task<ReadinessCheckResult> RunAsync(CancellationToken ct);
}

public interface IReadinessCheckTimeout
{
    TimeSpan Timeout { get; }
}
