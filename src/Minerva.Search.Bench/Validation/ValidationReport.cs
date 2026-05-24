using Minerva.Search.Bench.Common;

namespace Minerva.Search.Bench.Validation;

public static class ValidationReport
{
    public static void Print(
        IReadOnlyList<ValidationIssue> issues,
        int entriesParsed,
        int sourceIdsChecked,
        string collection,
        TextWriter output)
    {
        if (issues.Count == 0)
        {
            output.WriteLine(
                $"validation passed: {entriesParsed} entries, " +
                $"{sourceIdsChecked} gold_sources resolved against collection '{collection}'");
            return;
        }

        // Group by (LineNumber, EntryId) preserving line order.
        var groups = issues
            .GroupBy(i => (i.LineNumber, i.EntryId))
            .OrderBy(g => g.Key.LineNumber);

        foreach (var group in groups)
        {
            var header = group.Key.EntryId is not null
                ? $"[entry {group.Key.EntryId} (line {group.Key.LineNumber})]"
                : $"[line {group.Key.LineNumber}]";
            output.WriteLine(header);
            foreach (var issue in group)
                output.WriteLine($"- {issue.Message}");
            output.WriteLine();
        }

        var groupCount = issues
            .Select(i => (i.LineNumber, i.EntryId))
            .Distinct()
            .Count();
        output.WriteLine(
            $"validation failed: {groupCount} entries with problems, " +
            $"{issues.Count} issues total");
    }
}
