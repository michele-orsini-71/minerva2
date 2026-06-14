namespace Minerva.Models;

public record Collection(
    string Name,
    string? Description,
    CollectionProvenance Provenance,
    Dictionary<string, object>? Client = null,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset LastUpdatedAt = default)
{
    public string EmbeddingModel => Provenance.Invariants.EmbeddingModel;
    public int EmbeddingDimension => Provenance.Invariants.EmbeddingDimension;
}
