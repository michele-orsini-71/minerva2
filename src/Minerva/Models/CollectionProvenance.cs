namespace Minerva.Models;

public sealed record CollectionProvenance(
    CollectionInvariants Invariants,
    CollectionLastRun LastRun);

public sealed record CollectionInvariants(
    string EmbeddingModel,
    int EmbeddingDimension,
    ChunkerType ChunkerType,
    int TargetChunkSize,
    int ChunkOverlap);

public sealed record CollectionLastRun(
    string IngestorVersion,
    string SchemaVersion);
