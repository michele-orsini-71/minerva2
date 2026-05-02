namespace Minerva.Exceptions;

public sealed record PreflightFailure(string Stage, string Reason, Exception? Cause = null);

public sealed class MinervaStartupException : MinervaException
{
    public IReadOnlyList<PreflightFailure> Failures { get; }

    public MinervaStartupException(IReadOnlyList<PreflightFailure> failures)
        : base(BuildSummary(failures))
    {
        Failures = failures;
    }

    private static string BuildSummary(IReadOnlyList<PreflightFailure> failures)
    {
        if (failures.Count == 0) return "Minerva startup failed.";
        var lines = failures.Select(f => $"  - [{f.Stage}] {f.Reason}");
        return $"Minerva startup failed with {failures.Count} issue(s):\n{string.Join("\n", lines)}";
    }
}
