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
        var sourceIdsChecked = 0;
        foreach (var entry in pure.Entries)
        {
            foreach (var src in entry.GoldSources)
            {
                sourceIdsChecked++;
                if (!await engine.SourceIdExistsAsync(collection, src, ct))
                {
                    allIssues.Add(new ValidationIssue(entry.LineNumber, entry.Id,
                        $"gold_source not found in collection '{collection}': '{src}'"));
                }
            }
        }

        ValidationReport.Print(
            allIssues, pure.Entries.Count, sourceIdsChecked, collection, output);
        return allIssues.Count == 0 ? 0 : 2;
    }
}
