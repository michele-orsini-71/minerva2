using Minerva.Search.Bench.Common;

namespace Minerva.Search.Bench.Validation;

public static class GoldSourceChecker
{
    public static async Task<IReadOnlyList<ValidationIssue>> CheckAsync(
        ISearchEngine engine,
        string collection,
        IReadOnlyList<ParsedEntry> entries,
        CancellationToken ct = default)
    {
        var issues = new List<ValidationIssue>();
        foreach (var entry in entries)
            foreach (var src in entry.GoldSources)
                if (!await engine.SourceIdExistsAsync(collection, src, ct))
                    issues.Add(new ValidationIssue(entry.LineNumber, entry.Id,
                        $"gold_source not found in collection '{collection}': '{src}'"));
        return issues;
    }
}
