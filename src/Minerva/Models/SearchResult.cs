namespace Minerva.Models;

public record SearchResult(
    string ChunkId,
    string SourceId,
    string CollectionName,
    string Content,
    double Score,
    Dictionary<string, object>? Metadata = null,
    string? ContextBefore = null,
    string? ContextAfter = null);
