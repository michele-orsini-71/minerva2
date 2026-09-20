using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Providers;
using Minerva.Search;

namespace Minerva;

public static class MinervaSearchBuilder
{
    public static async Task<ISearchEngine> CreateAsync(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken ct = default)
    {
        // Phase 1: bind + validate options (both Minerva and Search sections).
        var options = MinervaSearchOptionsBinder.Bind(configuration);

        // Phase 2: construct (no I/O). Options arrive pre-validated.
        var core = MinervaCore.Build(options.ConnectionString, options.Embedding, loggerFactory);

        var vectorSearch = new VectorSearch(core.ChunkQuery);
        var fullTextSearch = new FullTextSearch(core.ChunkQuery);
        var contextExpander = new ContextExpander(core.ChunkQuery);
        HttpRerankerProvider? rerankerProvider = null;
        Reranker? reranker = null;
        HttpRerankerProvider? cascadeProvider = null;
        Reranker? cascadeReranker = null;
        if (options.Reranker is not null)
        {
            rerankerProvider = new HttpRerankerProvider(
                options.Reranker.Model, new Uri(options.Reranker.BaseUrl));
            reranker = new Reranker(rerankerProvider);
        }

        if (options.CascadeReranker is not null) 
        {
            cascadeProvider = new HttpRerankerProvider(
                options.CascadeReranker.Model, new Uri(options.CascadeReranker.BaseUrl));
            cascadeReranker = new Reranker(cascadeProvider);
        }

        var searchPipeline = new SearchPipeline(
            core.EmbeddingService,
            vectorSearch,
            fullTextSearch,
            contextExpander,
            reranker,
            loggerFactory.CreateLogger<SearchPipeline>(),
            cascadeReranker);

        var defaults = new SearchOptions
        {
            TopK = options.TopK,
            HybridAlpha = options.HybridAlpha,
            CandidatePoolSize = options.CandidatePoolSize,
            ExpandContext = options.ExpandContext,
            EnableReranker = options.EnableReranker,
            RerankDepth = options.RerankDepth,
            CascadeDepth = options.CascadeDepth
        };

        // Phase 3: preflight — DB + embedding, plus reranker if configured.
        var failures = new List<PreflightFailure>(await core.PreflightAsync(ct));

        if (rerankerProvider is not null && await rerankerProvider.PreflightAsync(ct) is { } rerankFailure)
            failures.Add(rerankFailure);

        if (cascadeProvider is not null && await cascadeProvider.PreflightAsync(ct) is { } cascadeFailure)
            failures.Add(cascadeFailure);

        if (failures.Count > 0)
            throw new MinervaStartupException(failures);

        // Phase 4: schema init.
        await core.SchemaInitializer.InitializeAsync(ct);

        return new MinervaSearchEngine(searchPipeline, core.Collections, core.SourceCatalog, defaults);
    }
}
