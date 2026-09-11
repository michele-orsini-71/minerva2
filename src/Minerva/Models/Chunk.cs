namespace Minerva.Models;

public record Chunk(
    string Id,
    string SourceId,
    string CollectionName,
    int ChunkIndex,
    string Content,
    string ContentHash,
    string? PrevChunkId = null,
    string? NextChunkId = null);
