using Microsoft.Extensions.Configuration;
using Minerva.Models;

namespace Minerva.Configuration;

public static class ContextBudgetOptionsBinder
{
    public static ContextBudgetOptions Bind(IConfiguration section)
    {
        var raw = new RawContextBudgetOptions();
        section.Bind(raw);

        var failures = new List<OptionsFailure>();
        var result = TryBuild(raw, stagePrefix: "", failures);
        if (failures.Count > 0)
            throw new OptionsValidationException(failures);
        return result!;
    }

    internal static ContextBudgetOptions? TryBuild(
        RawContextBudgetOptions raw, string stagePrefix, List<OptionsFailure> failures)
    {
        int before = failures.Count;

        BinderHelpers.ValidateRequiredPositiveInt(raw.MaxContextTokens, stagePrefix + "MaxContextTokens", failures);
        BinderHelpers.ValidateRequiredNonNegativeInt(raw.ReservedTokens, stagePrefix + "ReservedTokens", failures);

        if (raw.MaxContextTokens is int max && max > 0
            && raw.ReservedTokens is int reserved && reserved >= 0
            && reserved >= max)
        {
            failures.Add(new OptionsFailure(
                stagePrefix + "ReservedTokens",
                $"must be < MaxContextTokens (got {reserved} >= {max})."));
        }

        if (raw.CharsPerToken is null)
            failures.Add(new OptionsFailure(stagePrefix + "CharsPerToken", "is required."));
        else if (raw.CharsPerToken <= 0)
            failures.Add(new OptionsFailure(
                stagePrefix + "CharsPerToken",
                $"must be > 0 (got {raw.CharsPerToken})."));

        if (raw.SafetyFactor is null)
            failures.Add(new OptionsFailure(stagePrefix + "SafetyFactor", "is required."));
        else if (raw.SafetyFactor <= 0 || raw.SafetyFactor > 1)
            failures.Add(new OptionsFailure(
                stagePrefix + "SafetyFactor",
                $"must be in (0, 1] (got {raw.SafetyFactor})."));

        if (failures.Count > before) return null;

        return new ContextBudgetOptions
        {
            MaxContextTokens = raw.MaxContextTokens!.Value,
            ReservedTokens = raw.ReservedTokens!.Value,
            CharsPerToken = raw.CharsPerToken!.Value,
            SafetyFactor = raw.SafetyFactor!.Value,
        };
    }
}

internal sealed class RawContextBudgetOptions
{
    public int? MaxContextTokens { get; set; }
    public int? ReservedTokens { get; set; }
    public double? CharsPerToken { get; set; }
    public double? SafetyFactor { get; set; }
}
