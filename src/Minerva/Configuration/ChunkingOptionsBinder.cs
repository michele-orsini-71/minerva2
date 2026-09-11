using Microsoft.Extensions.Configuration;
using Minerva.Models;

namespace Minerva.Configuration;

public static class ChunkingOptionsBinder
{
    public static ChunkingOptions Bind(IConfiguration section)
    {
        var raw = new RawChunkingOptions();
        section.Bind(raw);

        var failures = new List<OptionsFailure>();
        var result = TryBuild(raw, stagePrefix: "", failures);
        if (failures.Count > 0)
            throw new OptionsValidationException(failures);
        return result!;
    }

    internal static ChunkingOptions? TryBuild(
        RawChunkingOptions raw, string stagePrefix, List<OptionsFailure> failures)
    {
        int before = failures.Count;

        BinderHelpers.ValidateRequiredPositiveInt(raw.TargetChunkSize, stagePrefix + "TargetChunkSize", failures);
        BinderHelpers.ValidateRequiredNonNegativeInt(raw.ChunkOverlap, stagePrefix + "ChunkOverlap", failures);
        if (raw.TargetChunkSize is int t && t > 0 && raw.ChunkOverlap is int o && o >= 0 && o >= t)
            failures.Add(new OptionsFailure(
                stagePrefix + "ChunkOverlap",
                $"must be < TargetChunkSize (got {o} >= {t})."));

        ChunkerType chunkerType = default;
        bool chunkerValid = false;
        if (string.IsNullOrWhiteSpace(raw.ChunkerType))
        {
            failures.Add(new OptionsFailure(stagePrefix + "ChunkerType", "is required."));
        }
        else if (!Enum.TryParse<ChunkerType>(raw.ChunkerType, ignoreCase: true, out chunkerType))
        {
            failures.Add(new OptionsFailure(
                stagePrefix + "ChunkerType",
                $"is not a valid ChunkerType ('{raw.ChunkerType}'). Valid values: Custom, SemanticKernel."));
        }
        else
        {
            chunkerValid = true;
        }

        if (failures.Count > before) return null;

        return new ChunkingOptions
        {
            TargetChunkSize = raw.TargetChunkSize!.Value,
            ChunkOverlap = raw.ChunkOverlap!.Value,
            ChunkerType = chunkerValid ? chunkerType : default,
        };
    }
}

internal sealed class RawChunkingOptions
{
    public int? TargetChunkSize { get; set; }
    public int? ChunkOverlap { get; set; }
    public string? ChunkerType { get; set; }
}
