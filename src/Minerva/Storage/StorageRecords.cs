namespace Minerva.Storage;

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
    int ChunkIndex,
    string Content,
    double Score,
    Dictionary<string, object>? Metadata = null);
