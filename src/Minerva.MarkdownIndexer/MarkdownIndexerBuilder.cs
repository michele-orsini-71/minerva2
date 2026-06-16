using Microsoft.Extensions.Logging;
using Minerva.Exceptions;

namespace Minerva.MarkdownIndexer;

public static class MarkdownIndexerBuilder
{
    public static Task<MarkdownIndexer> CreateAsync(
        IndexerOptions options,
        IIngestEngine engine,
        ISearchEngine searchEngine,
        ILoggerFactory loggerFactory,
        CancellationToken ct = default)
    {
        // Phase 2: construct (no I/O). Options arrive pre-validated from the binder.
        var scanner = new MarkdownScanner(options);

        // Phase 3: preflight (env). Collection compatibility lives inside engine.IngestAsync.
        var preflightFailures = RunPreflight(options);
        if (preflightFailures.Count > 0)
            throw new MinervaStartupException(preflightFailures);

        return Task.FromResult(new MarkdownIndexer(
            scanner,
            engine,
            searchEngine,
            options,
            loggerFactory.CreateLogger<MarkdownIndexer>()));
    }

    private static List<PreflightFailure> RunPreflight(IndexerOptions options)
    {
        var failures = new List<PreflightFailure>();

        if (!Directory.Exists(options.RootPath))
        {
            failures.Add(new PreflightFailure(
                "Indexer.RootPath",
                $"Root path '{options.RootPath}' does not exist."));
            return failures;
        }

        try
        {
            using var enumerator = Directory.EnumerateFileSystemEntries(options.RootPath).GetEnumerator();
            enumerator.MoveNext();
        }
        catch (UnauthorizedAccessException ex)
        {
            failures.Add(new PreflightFailure(
                "Indexer.RootPath",
                $"Process lacks read permission on '{options.RootPath}'.",
                ex));
        }

        return failures;
    }
}
