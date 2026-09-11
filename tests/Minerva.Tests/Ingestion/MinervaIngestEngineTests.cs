using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Collections;
using Minerva.Exceptions;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Utilities;
using NSubstitute;

namespace Minerva.Tests.Ingestion;

[Trait("Category", "Ingestion")]
public class MinervaIngestEngineTests
{
    private const string CollectionName = "test-collection";
    private static readonly float[] SampleVector = [0.1f, 0.2f, 0.3f];

    private sealed record TestBed(MinervaIngestEngine Engine, IChunkWriter Repo);

    // Real pipeline on top of substitutes; the embedder throws for documents whose
    // source id is in failingSourceIds, which is how a provider failure surfaces.
    private static TestBed CreateEngine(
        IReadOnlySet<string> failingSourceIds,
        IReadOnlyDictionary<string, string>? existing = null)
    {
        var chunker = Substitute.For<IDocumentChunker>();
        chunker.Chunk(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => (IReadOnlyList<Chunk>)
                [MakeChunk(ci.ArgAt<string>(0), ci.ArgAt<string>(1), ci.ArgAt<string>(2))]);

        var embedder = Substitute.For<IEmbeddingService>();
        embedder.EmbedAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var texts = ci.Arg<IReadOnlyList<string>>();
                if (texts.Any(failingSourceIds.Contains))
                    throw new ProviderUnavailableException("LLM API request failed (HTTP 400): Model unloaded.");
                return Task.FromResult((IReadOnlyList<float[]>)texts.Select(_ => SampleVector).ToArray());
            });

        var repo = Substitute.For<IChunkWriter>();
        repo.GetSourceIdsAndHashesAsync(CollectionName, Arg.Any<CancellationToken>())
            .Returns(existing ?? new Dictionary<string, string>());

        var pipeline = new IngestionPipeline(
            chunker, embedder, repo, NullLogger<IngestionPipeline>.Instance);

        var chunking = new ChunkingOptions
        {
            TargetChunkSize = 100,
            ChunkOverlap = 0,
            ChunkerType = ChunkerType.Custom,
        };

        var engine = new MinervaIngestEngine(
            pipeline,
            Substitute.For<ICollectionService>(),
            repo,
            configuredEmbeddingModel: "test-embedder",
            Substitute.For<IEmbeddingDimensionProvider>(),
            chunking,
            schemaVersion: "1",
            NullLogger<MinervaIngestEngine>.Instance);

        return new TestBed(engine, repo);
    }

    // Document text doubles as source id so the embedder can tell documents apart.
    private static Document Doc(string sourceId) => new(sourceId, sourceId, sourceId);

    private static Chunk MakeChunk(string collection, string sourceId, string content) => new(
        Id: HashHelper.GenerateChunkId(collection, sourceId, 0),
        SourceId: sourceId,
        CollectionName: collection,
        ChunkIndex: 0,
        Content: content,
        ContentHash: HashHelper.ComputeContentHash(content));

    private static async IAsyncEnumerable<Document> Docs(params string[] sourceIds)
    {
        foreach (var id in sourceIds)
        {
            yield return Doc(id);
            await Task.Yield();
        }
    }

    private static Task<IngestionResult> Ingest(TestBed bed, params string[] sourceIds) =>
        bed.Engine.IngestAsync(CollectionName, new ClientProvenance("test", []), Docs(sourceIds));

    [Fact]
    public async Task IngestAsync_ProviderFailure_SkipsDocumentAndContinues()
    {
        var bed = CreateEngine(failingSourceIds: new HashSet<string> { "b" });

        var result = await Ingest(bed, "a", "b", "c");

        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Deleted);
    }

    [Fact]
    public async Task IngestAsync_ProviderFailure_DoesNotDeleteAlreadyIndexedDocument()
    {
        var existing = new Dictionary<string, string> { ["b"] = "stale-hash" };
        var bed = CreateEngine(failingSourceIds: new HashSet<string> { "b" }, existing);

        var result = await Ingest(bed, "a", "b");

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Deleted);
        await bed.Repo.DidNotReceive()
            .DeleteBySourceIdAsync(CollectionName, "b", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_ConsecutiveFailures_ResetOnSuccess()
    {
        var bed = CreateEngine(failingSourceIds: new HashSet<string> { "f1", "f2", "f3", "f4", "f5", "f6" });

        var result = await Ingest(bed, "f1", "f2", "f3", "f4", "ok", "f5", "f6");

        Assert.Equal(1, result.Added);
        Assert.Equal(6, result.Failed);
    }

    [Fact]
    public async Task IngestAsync_FiveConsecutiveFailures_AbortsWithPartialCounts()
    {
        var bed = CreateEngine(failingSourceIds: new HashSet<string> { "f1", "f2", "f3", "f4", "f5" });

        var ex = await Assert.ThrowsAsync<IngestionAbortedException>(
            () => Ingest(bed, "a", "f1", "f2", "f3", "f4", "f5", "never-reached"));

        Assert.Equal(1, ex.Ingested);
        Assert.Equal(5, ex.Failed);
        Assert.IsType<ProviderUnavailableException>(ex.InnerException);
    }

    [Fact]
    public async Task IngestAsync_Abort_SkipsRemovalOfUnseenDocuments()
    {
        var existing = new Dictionary<string, string> { ["stale"] = "hash" };
        var bed = CreateEngine(failingSourceIds: new HashSet<string> { "f1", "f2", "f3", "f4", "f5" }, existing);

        await Assert.ThrowsAsync<IngestionAbortedException>(
            () => Ingest(bed, "f1", "f2", "f3", "f4", "f5", "stale"));

        await bed.Repo.DidNotReceive()
            .DeleteBySourceIdAsync(CollectionName, "stale", Arg.Any<CancellationToken>());
    }
}
