namespace Minerva.MarkdownIndexer;

public sealed class ProvenanceMalformedException : MarkdownIndexerException
{
    public ProvenanceMalformedException()
        : base("collection claims kind markdown-indexer but has no readable source root — provenance is malformed")
    {

    }
}