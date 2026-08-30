namespace Minerva.Models;

public record ChunkWithEmbedding(
    string Id,
    string SourceId,
    string CollectionName,
    int ChunkIndex,
    string Content,
    string ContentHash,
    float[] Embedding,
    string? ContextualPrefix = null,
    string? PrevChunkId = null,
    string? NextChunkId = null,
    Dictionary<string, object>? Metadata = null);

public record ChunkRecord(
    string Id,
    string SourceId,
    string CollectionName,
    int ChunkIndex,
    string Content,
    string ContentHash,
    string? ContextualPrefix = null,
    string? PrevChunkId = null,
    string? NextChunkId = null,
    Dictionary<string, object>? Metadata = null);

public record ChunkSearchRecord(
    string Id,
    string SourceId,
    string CollectionName,
    int ChunkIndex,
    string? ContextualPrefix,
    string Content,
    double RawScore,
    string? PrevChunkId = null,
    string? NextChunkId = null,
    Dictionary<string, object>? Metadata = null);
