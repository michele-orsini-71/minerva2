using System.Text.Json;
using Tomlyn;

namespace Minerva.Search.Bench.Sweep;

public sealed record SweepLoadResult(SweepConfig? Config, IReadOnlyList<string> Errors);

public static class SweepConfigLoader
{
    private static readonly IReadOnlySet<string> AllowedKnobs =
        new HashSet<string> { "top_k", "hybrid_alpha" };

    private static readonly TomlSerializerOptions TomlOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static SweepLoadResult Load(string toml)
    {
        SweepConfig? config;
        try
        {
            config = TomlSerializer.Deserialize<SweepConfig>(toml, TomlOptions);
        }
        catch (TomlException ex)
        {
            return new SweepLoadResult(null, [$"Malformed TOML: {ex.Message}"]);
        }

        if (config is null)
            return new SweepLoadResult(null, ["Malformed TOML: empty or unparseable document."]);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.Dataset))
            errors.Add("'dataset' is required and must be non-empty.");

        if (string.IsNullOrWhiteSpace(config.Collection))
            errors.Add("'collection' is required and must be non-empty.");

        if (config.Matrix.Count == 0)
            errors.Add("'[matrix]' is required and must declare at least one knob.");

        foreach (var (knob, values) in config.Matrix)
        {
            if (!AllowedKnobs.Contains(knob))
                errors.Add($"unknown matrix knob '{knob}'. Allowed: {string.Join(", ", AllowedKnobs)}.");

            if (values.Count == 0)
                errors.Add($"matrix knob '{knob}' must be a non-empty list.");
        }

        return errors.Count > 0
            ? new SweepLoadResult(null, errors)
            : new SweepLoadResult(config, []);
    }
}
