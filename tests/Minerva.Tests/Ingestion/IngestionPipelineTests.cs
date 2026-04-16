using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Configuration;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Storage;
using Minerva.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Minerva.Tests.Ingestion;

[Trait("Category", "Ingestion")]
public class IngestionPipelineTests
{
    private static readonly float[] SampleVector = [0.1f, 0.2f, 0.3f];
    private const string CollectionName = "test-collection";

    private static (IngestionPipeline pipeline, IChunkRepository repo) CreatePipeline(
        bool withSummarizer = false,
        bool withContextualizer = false)
    {
        var chunker = new DocumentChunker(new ChunkingOptions
        {
            TargetChunkSize = 1200,
            ChunkOverlap = 200,
            LargeDocumentThreshold = 8000,
        });

        var embeddingGenerator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        embeddingGenerator.GenerateAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<EmbeddingGenerationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var texts = callInfo.Arg<IEnumerable<string>>().ToList();
                var result = new GeneratedEmbeddings<Embedding<float>>();
                foreach (var _ in texts)
                    result.Add(new Embedding<float>(SampleVector));
                return Task.FromResult(result);
            });

        var embeddingService = new EmbeddingService(
            embeddingGenerator, batchSize: 100,
            NullLogger<EmbeddingService>.Instance);

        var chunkRepo = Substitute.For<IChunkRepository>();
        chunkRepo.GetContentHashAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        chunkRepo.UpsertChunksAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ChunkWithEmbedding>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        DocumentSummarizer? summarizer = null;
        ChunkContextualizer? contextualizer = null;

        if (withSummarizer || withContextualizer)
        {
            var chatClient = Substitute.For<IChatClient>();
            chatClient.GetResponseAsync(
                    Arg.Any<IEnumerable<ChatMessage>>(),
                    Arg.Any<ChatOptions?>(),
                    Arg.Any<CancellationToken>())
                .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A summary.")));

            if (withSummarizer)
                summarizer = new DocumentSummarizer(chatClient);
            if (withContextualizer)
                contextualizer = new ChunkContextualizer(chatClient);
        }

        var pipeline = new IngestionPipeline(
            chunker, embeddingService, summarizer, contextualizer,
            chunkRepo, NullLogger<IngestionPipeline>.Instance);

        return (pipeline, chunkRepo);
    }

    [Fact]
    public async Task IngestAsync_UnchangedDocument_ReturnsUnchanged()
    {
        var (pipeline, repo) = CreatePipeline();
        var doc = new Document("src1", "Title", "Some content here.");

        // Simulate stored hash matching document content
        var contentHash = HashHelper.ComputeContentHash(doc.Text);
        repo.GetContentHashAsync(CollectionName, "src1", Arg.Any<CancellationToken>())
            .Returns(contentHash);

        var result = await pipeline.IngestAsync(CollectionName, doc);

        Assert.Equal(1, result.Unchanged);
        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Updated);

        // Should NOT call UpsertChunksAsync
        await repo.DidNotReceive().UpsertChunksAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<ChunkWithEmbedding>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_NewDocument_ReturnsAdded()
    {
        var (pipeline, _) = CreatePipeline();
        var doc = new Document("src1", "Title", "# Header\nSome content here.");

        var result = await pipeline.IngestAsync(CollectionName, doc);

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Unchanged);
    }

    [Fact]
    public async Task IngestAsync_ChangedDocument_ReturnsUpdated()
    {
        var (pipeline, repo) = CreatePipeline();
        var doc = new Document("src1", "Title", "# Header\nNew content.");

        // Return a different hash to simulate existing document
        repo.GetContentHashAsync(CollectionName, "src1", Arg.Any<CancellationToken>())
            .Returns("old-hash-that-wont-match");

        var result = await pipeline.IngestAsync(CollectionName, doc);

        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Unchanged);
    }

    [Fact]
    public async Task IngestAsync_SkipsSummarizationWhenDisabled()
    {
        var (pipeline, repo) = CreatePipeline(withSummarizer: false, withContextualizer: false);
        var doc = new Document("src1", "Title", "# Header\nContent.");

        var result = await pipeline.IngestAsync(CollectionName, doc);

        Assert.Equal(1, result.Added);

        // Verify chunks were upserted without contextual prefixes
        await repo.Received(1).UpsertChunksAsync(
            CollectionName, "src1",
            Arg.Is<IReadOnlyList<ChunkWithEmbedding>>(
                chunks => chunks.All(c => c.ContextualPrefix == null)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_AdjacencyPointersAreCorrect()
    {
        var (pipeline, repo) = CreatePipeline();

        // Build a document that produces multiple chunks
        var text = "# Section 1\nContent one.\n\n# Section 2\nContent two.\n\n# Section 3\nContent three.";
        var doc = new Document("src1", "Title", text);

        await pipeline.IngestAsync(CollectionName, doc);

        await repo.Received(1).UpsertChunksAsync(
            CollectionName, "src1",
            Arg.Is<IReadOnlyList<ChunkWithEmbedding>>(chunks =>
                chunks.Count >= 3
                && chunks[0].PrevChunkId == null
                && chunks[0].NextChunkId == chunks[1].Id
                && chunks[1].PrevChunkId == chunks[0].Id
                && chunks[1].NextChunkId == chunks[2].Id
                && chunks[chunks.Count - 1].NextChunkId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_EmbeddingFailure_DoesNotUpsert()
    {
        var embeddingGenerator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        embeddingGenerator.GenerateAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<EmbeddingGenerationOptions?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Embedding API down"));

        var chunkRepo = Substitute.For<IChunkRepository>();
        chunkRepo.GetContentHashAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var chunker = new DocumentChunker(new ChunkingOptions());
        var embeddingService = new EmbeddingService(
            embeddingGenerator, batchSize: 100,
            NullLogger<EmbeddingService>.Instance);

        var pipeline = new IngestionPipeline(
            chunker, embeddingService, null, null,
            chunkRepo, NullLogger<IngestionPipeline>.Instance);

        var doc = new Document("src1", "Title", "# Header\nContent.");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.IngestAsync(CollectionName, doc));

        // Old data should be preserved — UpsertChunksAsync should NOT be called
        await chunkRepo.DidNotReceive().UpsertChunksAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<ChunkWithEmbedding>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_DelegatesToRepository()
    {
        var (pipeline, repo) = CreatePipeline();

        await pipeline.RemoveAsync(CollectionName, "src1");

        await repo.Received(1).DeleteBySourceIdAsync(CollectionName, "src1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_WithAttachments_IntegratesBeforeHashing()
    {
        var (pipeline, repo) = CreatePipeline();

        var attachments = new Dictionary<string, AttachmentDescription>
        {
            ["![[img.png]]"] = new("A photo of a cat"),
        };
        var doc = new Document("src1", "Title", "# Header\nSee ![[img.png]] here.", Attachments: attachments);

        await pipeline.IngestAsync(CollectionName, doc);

        // Verify the upserted chunk content includes the attachment description
        await repo.Received(1).UpsertChunksAsync(
            CollectionName, "src1",
            Arg.Is<IReadOnlyList<ChunkWithEmbedding>>(
                chunks => chunks.Any(c => c.Content.Contains("A photo of a cat"))),
            Arg.Any<CancellationToken>());
    }
}
