using Minerva.Exceptions;

namespace Minerva.Configuration;

public sealed record OptionsFailure(string Path, string Reason, Exception? Cause = null);

public sealed class OptionsValidationException : MinervaException
{
    public IReadOnlyList<OptionsFailure> Failures { get; }

    public OptionsValidationException(IReadOnlyList<OptionsFailure> failures)
        : base(BuildSummary(failures))
    {
        Failures = failures;
    }

    private static string BuildSummary(IReadOnlyList<OptionsFailure> failures)
    {
        if (failures.Count == 0) return "Options validation failed.";
        var lines = failures.Select(f => $"  - {f.Path}: {f.Reason}");
        return $"{failures.Count} option(s) invalid:\n{string.Join("\n", lines)}";
    }
}
