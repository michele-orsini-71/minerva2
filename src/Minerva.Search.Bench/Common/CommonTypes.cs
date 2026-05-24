namespace Minerva.Search.Bench.Common;
public sealed record ValidationIssue(int LineNumber, string? EntryId, string Message);

public sealed record ParsedEntry(
    int LineNumber,
    string Id,
    string Query,
    IReadOnlyList<string> GoldSources);

public sealed record PureValidationResult(
    IReadOnlyList<ParsedEntry> Entries,
    IReadOnlyList<ValidationIssue> Issues);