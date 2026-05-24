using Minerva;
using Minerva.Search.Bench.Common;
using Minerva.Search.Bench.Validation;

namespace Minerva.Search.Bench.Authoring;

public static class AuthorDatasetRunner
{
    public static async Task<int> RunAsync(
        ISearchEngine engine,
        string datasetPath,
        string collection,
        TextWriter output,
        CancellationToken ct = default)
    {
        IReadOnlyList<ParsedEntry> entries;

        if (File.Exists(datasetPath))
        {
            PureValidationResult pure;
            using (var reader = File.OpenText(datasetPath))
                pure = DatasetValidator.ValidatePure(reader);

            var allIssues = new List<ValidationIssue>(pure.Issues);
            if (allIssues.Count > 0)
            {
                output.WriteLine("Dataset has the following issues:");
                foreach (var issue in allIssues)
                {
                    output.WriteLine($"Line {issue.LineNumber}: {issue.Message}");
                }

                output.WriteLine("Please fix these issues before authoring.");
                return 2;
            }

            entries = pure.Entries;
        }
        else
        {
            entries = [];
        }
            
        return await AuthoringREPL.RunAsync(engine, datasetPath, collection, output, ct);
    }
}
