namespace Minerva.MarkdownIndexer;

public sealed class ProvenanceMalformedException : MarkdownIndexerException
{
    public ProvenanceMalformedException()
        : base($"collection claims kind {MarkdownIndexer.ClientProvenanceName} but has no readable source root — provenance is malformed")
    {

    }
}