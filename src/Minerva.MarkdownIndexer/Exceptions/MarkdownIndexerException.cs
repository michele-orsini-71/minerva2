namespace Minerva.MarkdownIndexer;

public abstract class MarkdownIndexerException : Exception
{
    protected MarkdownIndexerException(string reason) : base(reason) {}
}