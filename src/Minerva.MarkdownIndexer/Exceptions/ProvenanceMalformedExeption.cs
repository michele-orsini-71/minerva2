namespace Minerva.MarkdownIndexer;

public sealed class ProvenanceMalformedExeption : MarkdownIndexerException
{
    public ProvenanceMalformedExeption() 
        : base("collection claims kind markdown-indexer but has no readable source root — provenance is malformed")
    {

    }
}