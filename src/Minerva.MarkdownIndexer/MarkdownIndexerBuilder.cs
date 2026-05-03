using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Minerva.Exceptions;

namespace Minerva.MarkdownIndexer;

public static partial class MarkdownIndexerBuilder
{
    private static readonly Regex CollectionNameRegex = CollectionNamePattern();

    public static Task<MarkdownIndexer> CreateAsync(
        IndexerOptions options,
        IMinervaEngine engine,
        ILoggerFactory loggerFactory,
        bool forceRecreate,
        CancellationToken ct = default)
    {
        // Phase 1: validate options (sync, no I/O).
        var optionFailures = ValidateOptions(options);
        if (optionFailures.Count > 0)
            throw new MinervaStartupException(optionFailures);

        // Phase 2: construct (no I/O).
        var scanner = new MarkdownScanner(options);

        // Phase 3: preflight (env). Collection compatibility lives inside engine.IngestAsync.
        var preflightFailures = RunPreflight(options);
        if (preflightFailures.Count > 0)
            throw new MinervaStartupException(preflightFailures);

        return Task.FromResult(new MarkdownIndexer(
            scanner,
            engine,
            options.CollectionName,
            forceRecreate,
            loggerFactory.CreateLogger<MarkdownIndexer>()));
    }

    private static List<PreflightFailure> ValidateOptions(IndexerOptions options)
    {
        var failures = new List<PreflightFailure>();

        if (string.IsNullOrWhiteSpace(options.RootPath))
            failures.Add(new PreflightFailure("Indexer.Options", "RootPath is required."));

        if (string.IsNullOrWhiteSpace(options.CollectionName) || !CollectionNameRegex.IsMatch(options.CollectionName))
            failures.Add(new PreflightFailure(
                "Indexer.Options",
                $"CollectionName '{options.CollectionName}' is invalid: must match ^[a-zA-Z0-9][a-zA-Z0-9-]*$."));

        if (string.IsNullOrWhiteSpace(options.FilePattern))
            failures.Add(new PreflightFailure("Indexer.Options", "FilePattern is required."));

        return failures;
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

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9-]*$", RegexOptions.Compiled)]
    private static partial Regex CollectionNamePattern();
}
