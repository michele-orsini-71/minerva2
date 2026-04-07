namespace Minerva.Models;

public record Collection(
    string Name,
    string? Description,
    string EmbeddingModel,
    int EmbeddingDimension,
    Dictionary<string, object>? Metadata = null,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset LastUpdatedAt = default);
