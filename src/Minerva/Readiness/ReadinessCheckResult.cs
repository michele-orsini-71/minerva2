namespace Minerva.Readiness;

public record ReadinessCheckResult(
    string Name,
    ReadinessCategory Category,
    bool Passed,
    string Code,
    string? Message,
    string? Remediation);
