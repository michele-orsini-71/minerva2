namespace Minerva.Models;

public record Collection(
    string Name,
    string? Description,
    CollectionProvenance Provenance,
    ClientProvenance ClientProvenance,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset LastUpdatedAt = default)
{
    public string EmbeddingModel => Provenance.Invariants.EmbeddingModel;
    public int EmbeddingDimension => Provenance.Invariants.EmbeddingDimension;
}
