namespace Minerva.MarkdownIndexer;

public sealed class CollectionScopeChangeNotAllowedException(IReadOnlyList<string> changedFields)
    : MarkdownIndexerException(
        $"Collection source scope changed ({string.Join(", ", changedFields)}); " +
        "set AllowSourceScopeChange=true to reindex against the new scope.")
{
    public IReadOnlyList<string> ChangedFields { get; } = changedFields;
}
