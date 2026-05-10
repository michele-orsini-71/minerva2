using Microsoft.Extensions.Configuration;
using Minerva.Models;

namespace Minerva.Configuration;

public static class LlmProviderOptionsBinder
{
    public static LlmProviderOptions Bind(IConfiguration section)
    {
        var raw = new RawLlmProviderOptions();
        section.Bind(raw);

        var failures = new List<OptionsFailure>();
        var result = TryBuild(raw, stagePrefix: "", failures);
        if (failures.Count > 0)
            throw new OptionsValidationException(failures);
        return result!;
    }

    internal static LlmProviderOptions? TryBuild(
        RawLlmProviderOptions raw, string stagePrefix, List<OptionsFailure> failures)
    {
        int before = failures.Count;

        BinderHelpers.ValidateAbsoluteUri(raw.BaseUrl, stagePrefix + "BaseUrl", failures);
        BinderHelpers.ValidateRequiredString(raw.Model, stagePrefix + "Model", failures);
        BinderHelpers.ValidateRequiredPositiveInt(raw.Concurrency, stagePrefix + "Concurrency", failures);
        BinderHelpers.ValidateOptionalPositiveInt(raw.RequestsPerMinute, stagePrefix + "RequestsPerMinute", failures);

        ContextLengthProbe probe = default;
        bool probeValid = false;
        if (string.IsNullOrWhiteSpace(raw.ContextLengthProbe))
        {
            failures.Add(new OptionsFailure(stagePrefix + "ContextLengthProbe", "is required."));
        }
        else if (!Enum.TryParse<ContextLengthProbe>(raw.ContextLengthProbe, ignoreCase: true, out probe))
        {
            failures.Add(new OptionsFailure(
                stagePrefix + "ContextLengthProbe",
                $"is not a valid ContextLengthProbe ('{raw.ContextLengthProbe}'). Valid values: None, LMStudio, Ollama, LlamaCpp."));
        }
        else
        {
            probeValid = true;
        }

        ContextBudgetOptions? contextBudget = null;
        if (raw.ContextBudget is null)
            failures.Add(new OptionsFailure(stagePrefix + "ContextBudget", "is required."));
        else
            contextBudget = ContextBudgetOptionsBinder.TryBuild(
                raw.ContextBudget, stagePrefix + "ContextBudget.", failures);

        if (failures.Count > before) return null;

        return new LlmProviderOptions
        {
            BaseUrl = raw.BaseUrl!,
            Model = raw.Model!,
            ApiKey = string.IsNullOrWhiteSpace(raw.ApiKey) ? null : raw.ApiKey,
            Concurrency = raw.Concurrency!.Value,
            RequestsPerMinute = raw.RequestsPerMinute,
            ContextLengthProbe = probeValid ? probe : default,
            ContextBudget = contextBudget!,
        };
    }
}

internal sealed class RawLlmProviderOptions
{
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
    public int? Concurrency { get; set; }
    public int? RequestsPerMinute { get; set; }
    public string? ContextLengthProbe { get; set; }
    public RawContextBudgetOptions? ContextBudget { get; set; }
}
