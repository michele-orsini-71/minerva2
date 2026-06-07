using Minerva;
using Minerva.Search.Bench.Common;

namespace Minerva.Search.Bench.Validation;

public static class DatasetValidationRunner
{
    public static async Task<int> RunAsync(
        ISearchEngine engine,
        string datasetPath,
        string collection,
        TextWriter output,
        CancellationToken ct = default)
    {
        PureValidationResult pure;
        using (var reader = File.OpenText(datasetPath))
            pure = DatasetValidator.ValidatePure(reader);

        var allIssues = new List<ValidationIssue>(pure.Issues);
        allIssues.AddRange(
            await GoldSourceChecker.CheckAsync(engine, collection, pure.Entries, ct));
        var sourceIdsChecked = pure.Entries.Sum(e => e.GoldSources.Count);

        ValidationReport.Print(
            allIssues, pure.Entries.Count, sourceIdsChecked, collection, output);
        return allIssues.Count == 0 ? 0 : 2;
    }
}
