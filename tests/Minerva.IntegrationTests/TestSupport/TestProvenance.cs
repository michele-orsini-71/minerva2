using Minerva.Models;

namespace Minerva.IntegrationTests.TestSupport;

internal static class TestProvenance
{
    public static CollectionProvenance Create(
        string embeddingModel = "test-model",
        int embeddingDimension = 4,
        ChunkerType chunkerType = ChunkerType.Custom,
        int targetChunkSize = 600,
        int chunkOverlap = 100,
        string ingestorVersion = "0.0.0-test",
        string schemaVersion = "003_collection_provenance") =>
        new(
            new CollectionInvariants(
                embeddingModel, embeddingDimension, chunkerType, targetChunkSize, chunkOverlap),
            new CollectionLastRun(ingestorVersion, schemaVersion));

    public static ClientProvenance Client(
        string kind = "markdown-indexer",
        Dictionary<string, object>? data = null) =>
        new(kind, data ?? new Dictionary<string, object>());
}
