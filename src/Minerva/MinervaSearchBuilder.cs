using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Models;
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
        var searchPipeline = new SearchPipeline(
            core.EmbeddingService,
            vectorSearch,
            fullTextSearch,
            contextExpander,
            loggerFactory.CreateLogger<SearchPipeline>());

        var defaults = new SearchOptions
        {
            TopK = options.TopK,
            HybridAlpha = options.HybridAlpha,
            CandidatePoolSize = options.CandidatePoolSize,
            ExpandContext = options.ExpandContext,
        };

        // Phase 3: preflight — DB + embedding only. No LLM on the search path.
        var failures = new List<PreflightFailure>();

        var storageFailure = await core.DatabasePreflight.PreflightAsync(ct);
        if (storageFailure is not null)
            failures.Add(storageFailure);

        var embeddingFailure = await core.EmbeddingProvider.PreflightAsync(ct);
        if (embeddingFailure is not null)
            failures.Add(embeddingFailure);

        if (failures.Count > 0)
            throw new MinervaStartupException(failures);

        // Phase 4: schema init.
        await core.SchemaInitializer.InitializeAsync(ct);

        return new MinervaSearchEngine(searchPipeline, core.Collections, defaults);
    }
}
