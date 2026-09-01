using Microsoft.Extensions.Configuration;
using Minerva.Models;

namespace Minerva.Configuration;

public static class ContextualizationOptionsBinder
{
    public static ContextualizationOptions Bind(IConfiguration section)
    {
        var raw = new RawContextualizationOptions();
        section.Bind(raw);

        var failures = new List<OptionsFailure>();
        var result = TryBuild(raw, stagePrefix: "", failures);
        if (failures.Count > 0)
            throw new OptionsValidationException(failures);
        return result!;
    }

    internal static ContextualizationOptions? TryBuild(
        RawContextualizationOptions raw, string stagePrefix, List<OptionsFailure> failures)
    {
        int before = failures.Count;

        ContextualizationLevel contextualizationLevel = default;
        bool levelValid = false;
        if (string.IsNullOrWhiteSpace(raw.Level))
        {
            failures.Add(new OptionsFailure(stagePrefix + "Level", "is required."));
        }
        else if (!Enum.TryParse<ContextualizationLevel>(raw.Level, ignoreCase: true, out contextualizationLevel))
        {
            failures.Add(new OptionsFailure(
                stagePrefix + "Level",
                $"is not a valid ContextualizationLevel ('{raw.Level}'). Valid values: None, Breadcrumb, DocumentBrief, PerChunk."));
        }
        else
        {
            levelValid = true;
        }

        LlmProviderOptions? llmProviderOptions = null;
        if (raw.Llm is not null)
            llmProviderOptions = LlmProviderOptionsBinder.TryBuild(raw.Llm, stagePrefix + "Llm.", failures);

        if (contextualizationLevel > ContextualizationLevel.Breadcrumb && raw.Llm is null)
        {
            failures.Add(new OptionsFailure(stagePrefix + "Llm", "is required if Level is DocumentBrief or PerChunk."));
        }

        if (contextualizationLevel < ContextualizationLevel.DocumentBrief && raw.Llm is not null)
        {
            failures.Add(new OptionsFailure(stagePrefix + "Llm", "is specified but Level is Breadcrumb or None: it is not needed."));
        }

        if (failures.Count > before) return null;

        return new ContextualizationOptions
        {
            Level = levelValid ? contextualizationLevel : default,
            Llm = llmProviderOptions
        };
    }
}

internal sealed class RawContextualizationOptions
{
    public string? Level { get; set; }
    public RawLlmProviderOptions? Llm { get; set; }

}
