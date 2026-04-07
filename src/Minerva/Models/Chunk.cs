namespace Minerva.Models;

public record Chunk(
    string Id,
    string SourceId,
    string CollectionName,
    int ChunkIndex,
    string Content,
    string ContentHash,
    string? ContextualPrefix = null,
    string? PrevChunkId = null,
    string? NextChunkId = null);
