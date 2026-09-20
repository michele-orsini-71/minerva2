using System.Diagnostics;
using System.Text.Json;
using Tomlyn;

namespace Minerva.Search.Bench.Sweep;

public sealed record SweepLoadResult(SweepConfig? Config, IReadOnlyList<string> Errors);

public static class SweepConfigLoader
{
    private static readonly IReadOnlySet<string> AllowedKnobs =
        new HashSet<string> { "enable_reranker", "hybrid_alpha", "rerank_depth", "cascade_depth" };

    private static readonly TomlSerializerOptions TomlOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static SweepLoadResult Load(string toml)
    {
        RawSweepConfig? config;
        try
        {
            config = TomlSerializer.Deserialize<RawSweepConfig>(toml, TomlOptions);
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

        if (config.TopK is null)
            errors.Add("'top_k' is required.");

        if (config.CandidatePoolSize is null)
            errors.Add("'candidate_pool_size' is required.");

        if (config.Matrix.Count == 0)
            errors.Add("'[matrix]' is required and must declare at least one knob.");

        foreach (var (knob, values) in config.Matrix)
        {
            if (!AllowedKnobs.Contains(knob))
                errors.Add($"unknown matrix knob '{knob}'. Allowed: {string.Join(", ", AllowedKnobs)}.");

            if (values.Count == 0)
                errors.Add($"matrix knob '{knob}' must be a non-empty list.");
        }

        if (errors.Count > 0)
        {
            return new SweepLoadResult(null, errors);
        } else {
            int topK = config.TopK ?? throw new UnreachableException("validated above");
            int candidatePoolSize = config.CandidatePoolSize ?? throw new UnreachableException("validated above");

            SweepConfig runtimeConfig = new SweepConfig(config.Dataset, config.Collection, 
                topK, candidatePoolSize, config.Label, config.Matrix);

            return new SweepLoadResult(runtimeConfig, []);
        }
    }
}
