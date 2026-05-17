using Microsoft.Extensions.Configuration;
using Minerva.Models;

namespace Minerva.Configuration;

public static class MinervaSearchOptionsBinder
{
    public static MinervaSearchOptions Bind(IConfiguration configuration)
    {
        var rawMinerva = new RawMinervaSearchOptions();
        configuration.GetSection("Minerva").Bind(rawMinerva);

        var rawSearch = new RawSearchSectionOptions();
        var searchSection = configuration.GetSection("Search");
        if (!searchSection.Exists())
            rawSearch = null!;
        else
            searchSection.Bind(rawSearch);

        var failures = new List<OptionsFailure>();

        BinderHelpers.ValidateRequiredString(rawMinerva.ConnectionString, "Minerva.ConnectionString", failures);

        EmbeddingProviderOptions? embedding = null;
        if (rawMinerva.Embedding is null)
            failures.Add(new OptionsFailure("Minerva.Embedding", "section is required."));
        else
            embedding = EmbeddingProviderOptionsBinder.TryBuild(
                rawMinerva.Embedding, "Minerva.Embedding.", failures);

        int? topK = null;
        double? hybridAlpha = null;
        int? candidatePoolMultiplier = null;
        bool? expandContext = null;

        if (rawSearch is null)
        {
            failures.Add(new OptionsFailure("Search", "section is required."));
        }
        else
        {
            if (rawSearch.TopK is null)
                failures.Add(new OptionsFailure("Search.TopK", "is required."));
            else if (rawSearch.TopK <= 0)
                failures.Add(new OptionsFailure("Search.TopK", $"must be > 0 (got {rawSearch.TopK})."));
            else
                topK = rawSearch.TopK;

            if (rawSearch.HybridAlpha is null)
                failures.Add(new OptionsFailure("Search.HybridAlpha", "is required."));
            else if (rawSearch.HybridAlpha < 0 || rawSearch.HybridAlpha > 1)
                failures.Add(new OptionsFailure(
                    "Search.HybridAlpha",
                    $"must be in [0, 1] (got {rawSearch.HybridAlpha})."));
            else
                hybridAlpha = rawSearch.HybridAlpha;

            if (rawSearch.CandidatePoolMultiplier is null)
                failures.Add(new OptionsFailure("Search.CandidatePoolMultiplier", "is required."));
            else if (rawSearch.CandidatePoolMultiplier <= 0)
                failures.Add(new OptionsFailure(
                    "Search.CandidatePoolMultiplier",
                    $"must be > 0 (got {rawSearch.CandidatePoolMultiplier})."));
            else
                candidatePoolMultiplier = rawSearch.CandidatePoolMultiplier;

            if (rawSearch.ExpandContext is null)
                failures.Add(new OptionsFailure("Search.ExpandContext", "is required."));
            else
                expandContext = rawSearch.ExpandContext;
        }

        if (failures.Count > 0)
            throw new OptionsValidationException(failures);

        return new MinervaSearchOptions
        {
            ConnectionString = rawMinerva.ConnectionString!,
            Embedding = embedding!,
            TopK = topK!.Value,
            HybridAlpha = hybridAlpha!.Value,
            CandidatePoolMultiplier = candidatePoolMultiplier!.Value,
            ExpandContext = expandContext!.Value,
        };
    }
}

internal sealed class RawMinervaSearchOptions
{
    public string? ConnectionString { get; set; }
    public RawEmbeddingProviderOptions? Embedding { get; set; }
}

internal sealed class RawSearchSectionOptions
{
    public int? TopK { get; set; }
    public double? HybridAlpha { get; set; }
    public int? CandidatePoolMultiplier { get; set; }
    public bool? ExpandContext { get; set; }
}
