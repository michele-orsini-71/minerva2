namespace Minerva.MarkdownIndexer;

public sealed record IndexerOptions
{
    public required string RootPath { get; init; }
    public required string CollectionName { get; init; }
    public required IReadOnlyList<string> ExcludeDirectories { get; init; }
    public required bool AllowRecreateOnConfigMismatch { get; init; }
}
