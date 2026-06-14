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
        int maxSegmentChars = 8000,
        bool contextualizationEnabled = false,
        string? contextualizationModel = null,
        string? summarizerPromptVersion = null,
        string? contextualizerPromptVersion = null,
        string ingestorVersion = "0.0.0-test",
        string schemaVersion = "003_collection_provenance") =>
        new(
            new CollectionInvariants(
                embeddingModel, embeddingDimension, chunkerType, targetChunkSize,
                chunkOverlap, maxSegmentChars, contextualizationEnabled,
                contextualizationModel, summarizerPromptVersion, contextualizerPromptVersion),
            new CollectionLastRun(ingestorVersion, schemaVersion));
}
