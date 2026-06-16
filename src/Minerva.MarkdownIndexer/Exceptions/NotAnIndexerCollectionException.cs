namespace Minerva.MarkdownIndexer;

public sealed class NotAnIndexerCollectionException(string collectionKind) 
    : MarkdownIndexerException($"This is not a MarkdownIndexer created collection, creator is {collectionKind}.")
{
    public string CollectionKind { get; } = collectionKind;
}