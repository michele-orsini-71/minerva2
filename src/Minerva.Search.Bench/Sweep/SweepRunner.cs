using System.Globalization;
using System.Reflection;
using Minerva.Search.Bench.Common;
using Minerva.Search.Bench.Validation;

namespace Minerva.Search.Bench.Sweep;

public static class SweepDatasetRunner
{
    public static async Task<int> RunAsync(
        ISearchEngine engine,
        string sweepPath,
        string outputDir,
        TextWriter output,
        CancellationToken ct = default)
    {
        var loadResult = SweepConfigLoader.Load(File.ReadAllText(sweepPath));
        if (loadResult.Errors.Count > 0)
        {
            output.WriteLine("Sweep config has the following issues:");
            foreach (var error in loadResult.Errors)
                output.WriteLine($"- {error}");
            output.WriteLine("Please fix these issues before running.");
            return 2;
        }

        var sweep = loadResult.Config!;

        // The dataset path inside the sweep is resolved relative to the sweep file's
        // own directory, so a sweep and its dataset relocate together.
        var sweepDir = Path.GetDirectoryName(Path.GetFullPath(sweepPath)) ?? ".";
        var datasetPath = Path.Combine(sweepDir, sweep.Dataset);

        if (!File.Exists(datasetPath))
        {
            output.WriteLine($"Dataset file not found: {datasetPath}");
            return 2;
        }

        PureValidationResult validation;
        using (var reader = File.OpenText(datasetPath))
            validation = DatasetValidator.ValidatePure(reader);

        var issues = new List<ValidationIssue>(validation.Issues);
        issues.AddRange(
            await GoldSourceChecker.CheckAsync(engine, sweep.Collection, validation.Entries, ct));

        if (issues.Count > 0)
        {
            output.WriteLine("Dataset has the following issues:");
            foreach (var issue in issues)
                output.WriteLine($"Line {issue.LineNumber}: {issue.Message}");
            return 2;
        }

        var entries = validation.Entries;
        var cells = CellEnumerator.Enumerate(sweep.Matrix);
        var timestamp = DateTimeOffset.UtcNow;
        var benchVersion = BenchVersion();

        output.WriteLine(
            $"Running {entries.Count} queries x {cells.Count} cells against '{sweep.Collection}'...");

        var results = await SweepRunLoop.RunAsync(engine, sweep.Collection, entries, cells, ct);

        string leaf;
        try
        {
            leaf = OutputDirectory.Mint(outputDir, timestamp, sweep.Dataset);
        }
        catch (IOException ex)
        {
            output.WriteLine(ex.Message);
            return 2;
        }

        var collection = await engine.QueryCollectionInfoAsync(sweep.Collection, ct);
        if (collection == null)
        {
            output.WriteLine($"Collection '{sweep.Collection}' does not exist.");
            return 2;
        }

        RunJsonWriter.Write(Path.Combine(leaf, "run.json"), timestamp, benchVersion, sweep, cells, collection);
        MetricsCsvWriter.Write(
            Path.Combine(leaf, "metrics.csv"), sweep.Matrix.Keys.ToList(), results);
        DetailsJsonlWriter.Write(Path.Combine(leaf, "details.jsonl"), entries, results);

        var errorCount = results.Count(r => r.Error is not null);
        output.WriteLine($"Wrote {results.Count} result rows to {leaf}");
        if (errorCount > 0)
            output.WriteLine($"  ({errorCount} queries errored — see the 'error' column)");

        PrintCellSummary(output, cells, results);

        return 0;
    }

    // Aggregate Recall@K and MRR@10 per cell, averaged over the queries that
    // scored (errored queries carry no scores and are excluded from the mean).
    private static void PrintCellSummary(
        TextWriter output, IReadOnlyList<Cell> cells, IReadOnlyList<CellQueryResult> results)
    {
        output.WriteLine();
        output.WriteLine("Aggregate per cell (mean over scored queries):");

        foreach (var cell in cells)
        {
            var label = string.Join(" ", cell.Values.Select(kv =>
                $"{kv.Key}={Convert.ToString(kv.Value, CultureInfo.InvariantCulture)}"));

            var scores = results
                .Where(r => ReferenceEquals(r.Cell, cell) && r.Scores is not null)
                .Select(r => r.Scores!)
                .ToList();

            if (scores.Count == 0)
            {
                output.WriteLine($"  [{label}] no scored queries");
                continue;
            }

            output.WriteLine(
                $"  [{label}] " +
                $"R@5={scores.Average(s => s.RecallAt5):F3} " +
                $"R@10={scores.Average(s => s.RecallAt10):F3} " +
                $"R@20={scores.Average(s => s.RecallAt20):F3} " +
                $"MRR@10={scores.Average(s => s.RrAt10):F3} " +
                $"(n={scores.Count})");
        }
    }

    private static string BenchVersion()
        => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
}
