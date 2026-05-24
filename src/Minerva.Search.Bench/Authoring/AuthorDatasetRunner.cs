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
            
        output.WriteLine($"Authoring {datasetPath} against collection '{collection}'.");
        output.WriteLine("Commands: :search <q> [--n N], :pick <id|rank>, :undo, :quit  (empty line exits compose mode)");

        string? composeAnchor = null;   // null => top-level; non-null => compose mode anchored to this source-id

        while (!ct.IsCancellationRequested)
        {
            output.Write(composeAnchor is null ? "> " : $"({composeAnchor})> ");
            output.Flush();

            var line = Console.ReadLine();
            if (line is null) break;                       // EOF / Ctrl-D
            if (composeAnchor is not null && line.Length == 0) { composeAnchor = null; continue; }

            if (line.StartsWith(':'))
            {
                var space = line.IndexOf(' ');
                var cmd  = space < 0 ? line : line[..space];
                var rest = space < 0 ? ""   : line[(space + 1)..].Trim();

                switch (cmd)
                {
                    case ":quit": return 0;
                    case ":search": /* TODO */ break;
                    case ":pick":   /* TODO */ break;
                    case ":undo":   /* TODO */ break;
                    default: output.WriteLine($"Unknown command: {cmd}"); break;
                }
            }
            else if (composeAnchor is not null)
            {
                // TODO: append JSONL entry for (composeAnchor, line)
            }
            else
            {
                output.WriteLine("Type :search <query> to find a source.");
            }
        }
        return 0;
    }
}
