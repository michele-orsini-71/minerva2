using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Minerva.Tests.Ingestion;

[Trait("Category", "Ingestion")]
public class IngestionPipelineTests
{
    private static readonly float[] SampleVector = [0.1f, 0.2f, 0.3f];
    private const string CollectionName = "test-collection";
    private const string SourceId = "src1";

    private sealed record TestBed(
        IngestionPipeline Pipeline,
        IDocumentChunker Chunker,
        IEmbeddingService Embedder,
        ISourceWriter Repo);

    private static TestBed CreatePipeline()
    {
        var chunker = Substitute.For<IDocumentChunker>();
        chunker.Chunk(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => (IReadOnlyList<Chunk>)
                [MakeChunk(ci.ArgAt<string>(0), ci.ArgAt<string>(1), 0, ci.ArgAt<string>(2))]);

        var embedder = Substitute.For<IEmbeddingService>();
        embedder.EmbedAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(
                (IReadOnlyList<float[]>)ci.Arg<IReadOnlyList<string>>()
                    .Select(_ => SampleVector).ToArray()));

        var repo = Substitute.For<ISourceWriter>();

        var pipeline = new IngestionPipeline(
            chunker, embedder, repo, NullLogger<IngestionPipeline>.Instance);

        return new TestBed(pipeline, chunker, embedder, repo);
    }

    private static Chunk MakeChunk(
        string collection, string sourceId, int index, string content) => new(
            Id: HashHelper.GenerateChunkId(collection, sourceId, index),
            SourceId: sourceId,
            CollectionName: collection,
            ChunkIndex: index,
            Content: content,
            ContentHash: HashHelper.ComputeContentHash(content));

    [Fact]
    public async Task IngestAsync_UnchangedDocument_ReturnsUnchanged()
    {
        var bed = CreatePipeline();
        var doc = new Document(SourceId, "Title", "Some content here.");
        var contentHash = HashHelper.ComputeContentHash(doc.Text);

        var result = await bed.Pipeline.IngestAsync(CollectionName, doc, contentHash);

        Assert.Equal(1, result.Unchanged);
        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Updated);
        await bed.Repo.DidNotReceive().UpsertSourceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<IReadOnlyList<ChunkWithEmbedding>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_NewDocument_ReturnsAdded()
    {
        var bed = CreatePipeline();
        var doc = new Document(SourceId, "Title", "Some content here.");

        var result = await bed.Pipeline.IngestAsync(CollectionName, doc, storedContentHash: null);

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Unchanged);
    }

    [Fact]
    public async Task IngestAsync_ChangedDocument_ReturnsUpdated()
    {
        var bed = CreatePipeline();
        var doc = new Document(SourceId, "Title", "New content.");

        var result = await bed.Pipeline.IngestAsync(
            CollectionName, doc, storedContentHash: "old-hash-that-wont-match");

        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Unchanged);
    }

    [Fact]
    public async Task IngestAsync_AdjacencyPointersAreCorrect()
    {
        var bed = CreatePipeline();
        bed.Chunker.Chunk(CollectionName, SourceId, Arg.Any<string>())
            .Returns((IReadOnlyList<Chunk>)
            [
                MakeChunk(CollectionName, SourceId, 0, "one"),
                MakeChunk(CollectionName, SourceId, 1, "two"),
                MakeChunk(CollectionName, SourceId, 2, "three"),
            ]);
        var doc = new Document(SourceId, "Title", "text");

        await bed.Pipeline.IngestAsync(CollectionName, doc, storedContentHash: null);

        await bed.Repo.Received(1).UpsertSourceAsync(
            CollectionName, SourceId, "Title", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<IReadOnlyList<ChunkWithEmbedding>>(chunks =>
                chunks.Count == 3
                && chunks[0].PrevChunkId == null
                && chunks[0].NextChunkId == chunks[1].Id
                && chunks[1].PrevChunkId == chunks[0].Id
                && chunks[1].NextChunkId == chunks[2].Id
                && chunks[2].PrevChunkId == chunks[1].Id
                && chunks[2].NextChunkId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_EmbeddingFailure_DoesNotUpsert()
    {
        var bed = CreatePipeline();
        bed.Embedder.EmbedAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Embedding API down"));
        var doc = new Document(SourceId, "Title", "Content.");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bed.Pipeline.IngestAsync(CollectionName, doc, storedContentHash: null));

        await bed.Repo.DidNotReceive().UpsertSourceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<IReadOnlyList<ChunkWithEmbedding>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_DelegatesToRepository()
    {
        var bed = CreatePipeline();

        await bed.Pipeline.RemoveAsync(CollectionName, SourceId);

        await bed.Repo.Received(1).DeleteBySourceIdAsync(
            CollectionName, SourceId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_WithAttachments_IntegratesBeforeHashing()
    {
        var bed = CreatePipeline();
        var attachments = new Dictionary<string, AttachmentDescription>
        {
            ["![[img.png]]"] = new("A photo of a cat"),
        };
        var doc = new Document(
            SourceId, "Title", "See ![[img.png]] here.", Attachments: attachments);

        await bed.Pipeline.IngestAsync(CollectionName, doc, storedContentHash: null);

        await bed.Repo.Received(1).UpsertSourceAsync(
            CollectionName, SourceId, "Title", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<IReadOnlyList<ChunkWithEmbedding>>(
                chunks => chunks.Any(c => c.Content.Contains("A photo of a cat"))),
            Arg.Any<CancellationToken>());
    }

}
