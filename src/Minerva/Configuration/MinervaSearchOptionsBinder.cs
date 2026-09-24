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

        RerankerProviderOptions? reranker = null;
        RerankerProviderOptions? cascadeReranker = null;
        if (rawMinerva.Reranker is not null)
        {
            reranker = RerankerProviderOptionsBinder.TryBuild(
                rawMinerva.Reranker, "Minerva.Reranker.", failures);
            if (rawMinerva.CascadeReranker is not null)
            {
                cascadeReranker = RerankerProviderOptionsBinder.TryBuild(
                    rawMinerva.CascadeReranker, "Minerva.CascadeReranker.", failures);
            }
        }
        else
        {
            if (rawMinerva.CascadeReranker is not null)
            {
                failures.Add(new OptionsFailure("Minerva.CascadeReranker",
                "is defined while Reranker is not."));
            }
        }

        int? topK = null;
        double? hybridAlpha = null;
        int? candidatePoolSize = null;
        int? rerankDepth = null;
        int? cascadeRerankDepth = null;
        bool? expandContext = null;
        bool? enableReranker = null;

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

            if (rawSearch.EnableReranker is null)
                failures.Add(new OptionsFailure("Search.EnableReranker", "is required."));
            else
                enableReranker = rawSearch.EnableReranker;

            if (rawSearch.RerankDepth is not null)
            {
                if (rawSearch.RerankDepth <= 0)
                {
                    failures.Add(new OptionsFailure(
                        "Search.RerankDepth",
                        $"must be > 0 (got {rawSearch.RerankDepth})."));
                }
                else
                {
                    rerankDepth = rawSearch.RerankDepth;
                }
            }

            if (rawSearch.CascadeDepth is not null)
            {
                if (rawSearch.CascadeDepth < 0)
                {
                    failures.Add(new OptionsFailure(
                        "Search.CascadeDepth",
                        $"must be >= 0 (got {rawSearch.CascadeDepth})."));
                }
                else
                {
                    cascadeRerankDepth = rawSearch.CascadeDepth;
                }
            }

            if (rawSearch.CandidatePoolSize is null)
            {
                failures.Add(new OptionsFailure("Search.CandidatePoolSize", "is required."));
            }
            else
            {
                if (rawSearch.CandidatePoolSize <= 0)
                {
                    failures.Add(new OptionsFailure(
                        "Search.CandidatePoolSize",
                        $"must be > 0 (got {rawSearch.CandidatePoolSize})."));
                }
                else if (topK is not null && rawSearch.CandidatePoolSize < topK)
                {
                    failures.Add(new OptionsFailure(
                        "Search.CandidatePoolSize",
                        $"must be >= TopK (got {rawSearch.CandidatePoolSize}, TopK = {topK})."));
                }
                else
                {
                    candidatePoolSize = rawSearch.CandidatePoolSize;
                }
            }

            if (rawSearch.ExpandContext is null)
                failures.Add(new OptionsFailure("Search.ExpandContext", "is required."));
            else
                expandContext = rawSearch.ExpandContext;

        }

        ValidateRerankerConsistency(failures, enableReranker, rerankDepth, cascadeRerankDepth, reranker, cascadeReranker);

        if (failures.Count > 0)
            throw new OptionsValidationException(failures);

        return new MinervaSearchOptions
        {
            ConnectionString = rawMinerva.ConnectionString!,
            Embedding = embedding!,
            Reranker = reranker,
            CascadeReranker = cascadeReranker,
            TopK = topK!.Value,
            HybridAlpha = hybridAlpha!.Value,
            CandidatePoolSize = candidatePoolSize!.Value,
            ExpandContext = expandContext!.Value,
            RerankDepth = rerankDepth,
            CascadeDepth = cascadeRerankDepth,
            EnableReranker = enableReranker!.Value,
        };
    }

    static void ValidateRerankerConsistency(List<OptionsFailure> failures, bool? enableReranker,
        int? rerankerDepth, int? cascadeDepth, RerankerProviderOptions? reranker, RerankerProviderOptions? cascadeReranker)
    {
        if (cascadeDepth > 0)
        {
            if (cascadeReranker is null)
            {
                failures.Add(new OptionsFailure(
                    "Search.CascadeDepth",
                    "is defined but cascade reranker is not."));
            }

            if (rerankerDepth.HasValue)
            {
                if (cascadeDepth.Value >= rerankerDepth.Value)
                {
                    failures.Add(new OptionsFailure(
                        "Search.CascadeDepth",
                        "must be smaller than Search.RerankDepth"));
                }
            }
        }

        if (enableReranker == true && reranker is null)
        {
            failures.Add(new OptionsFailure(
                "Search.EnableReranker",
                "is true but the Minerva.Reranker section is missing. "
                + "Provide the reranker BaseUrl and Model, or set EnableReranker to false."));
        }

        if (cascadeReranker is not null && cascadeDepth is null)
        {
            failures.Add(new OptionsFailure(
                "Minerva.CascadeReranker",
                "is defined but cascade depth is not."));
        }
    }
}

internal sealed class RawMinervaSearchOptions
{
    public string? ConnectionString { get; set; }
    public RawEmbeddingProviderOptions? Embedding { get; set; }
    public RawRerankerProviderOptions? Reranker { get; set; }
    public RawRerankerProviderOptions? CascadeReranker { get; set; }
}

internal sealed class RawSearchSectionOptions
{
    public int? TopK { get; set; }
    public double? HybridAlpha { get; set; }
    public int? CandidatePoolSize { get; set; }
    public bool? ExpandContext { get; set; }
    public bool? EnableReranker { get; set; }
    public int? RerankDepth { get; set; }
    public int? CascadeDepth { get; set; }
}
