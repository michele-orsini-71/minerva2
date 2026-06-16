namespace Minerva.MarkdownIndexer;

public sealed class CollectionPathChangeNotAllowedException(string existingPath)
    : MarkdownIndexerException($"Collection source root changed; existing path is '{existingPath}'.")
{
    public string ExistingPath { get; } = existingPath;
}
