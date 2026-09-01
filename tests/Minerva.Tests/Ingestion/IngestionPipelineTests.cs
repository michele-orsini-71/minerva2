using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Exceptions;
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
        IChunkWriter Repo,
        IDocumentSummarizer? Summarizer,
        IChunkContextualizer? Contextualizer);

    private static TestBed CreatePipeline(
        ContextualizationLevel level = ContextualizationLevel.None)
    {
        // Mirror the builder's contract: summarizer at DocumentBrief and above,
        // contextualizer only at PerChunk.
        bool withSummarizer = level >= ContextualizationLevel.DocumentBrief;
        bool withContextualizer = level == ContextualizationLevel.PerChunk;

        var chunker = Substitute.For<IDocumentChunker>();
        chunker.SegmentDocument(Arg.Any<string>())
            .Returns(ci => (IReadOnlyList<string>)[ci.Arg<string>()]);
        chunker.Chunk(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => (IReadOnlyList<Chunk>)
                [MakeChunk(ci.ArgAt<string>(0), ci.ArgAt<string>(1), 0, ci.ArgAt<string>(2))]);
        chunker.ChunkSegment(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(ci => (IReadOnlyList<Chunk>)
                [MakeChunk(ci.ArgAt<string>(0), ci.ArgAt<string>(1),
                    ci.ArgAt<int>(3), ci.ArgAt<string>(2))]);

        var embedder = Substitute.For<IEmbeddingService>();
        embedder.EmbedAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(
                (IReadOnlyList<float[]>)ci.Arg<IReadOnlyList<string>>()
                    .Select(_ => SampleVector).ToArray()));

        var repo = Substitute.For<IChunkWriter>();

        IDocumentSummarizer? summarizer = null;
        if (withSummarizer)
        {
            summarizer = Substitute.For<IDocumentSummarizer>();
            summarizer.SummarizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns("A summary.");
        }

        IChunkContextualizer? contextualizer = null;
        if (withContextualizer)
        {
            contextualizer = Substitute.For<IChunkContextualizer>();
            contextualizer.ContextualizeAsync(
                    Arg.Any<string>(), Arg.Any<IReadOnlyList<Chunk>>(), Arg.Any<CancellationToken>())
                .Returns(ci => (IReadOnlyList<string>)ci.Arg<IReadOnlyList<Chunk>>()
                    .Select(_ => "ctx").ToArray());
        }

        var pipeline = new IngestionPipeline(
            chunker, embedder, summarizer, contextualizer, level,
            repo, NullLogger<IngestionPipeline>.Instance);

        return new TestBed(pipeline, chunker, embedder, repo, summarizer, contextualizer);
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
        await bed.Repo.DidNotReceive().UpsertChunksAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<ChunkWithEmbedding>>(), Arg.Any<CancellationToken>());
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
    public async Task IngestAsync_SkipsContextualizationWhenDisabled()
    {
        var bed = CreatePipeline(ContextualizationLevel.None);
        var doc = new Document(SourceId, "Title", "Content.");

        var result = await bed.Pipeline.IngestAsync(CollectionName, doc, storedContentHash: null);

        Assert.Equal(1, result.Added);
        await bed.Repo.Received(1).UpsertChunksAsync(
            CollectionName, SourceId,
            Arg.Is<IReadOnlyList<ChunkWithEmbedding>>(
                chunks => chunks.All(c => c.ContextualPrefix == null)),
            Arg.Any<CancellationToken>());
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

        await bed.Repo.Received(1).UpsertChunksAsync(
            CollectionName, SourceId,
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

        await bed.Repo.DidNotReceive().UpsertChunksAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<ChunkWithEmbedding>>(), Arg.Any<CancellationToken>());
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

        await bed.Repo.Received(1).UpsertChunksAsync(
            CollectionName, SourceId,
            Arg.Is<IReadOnlyList<ChunkWithEmbedding>>(
                chunks => chunks.Any(c => c.Content.Contains("A photo of a cat"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_LlmContextOverflow_PropagatesWithLayeredEnrichment()
    {
        // Real DocumentSummarizer + real IngestionPipeline + fake ILlmClient that throws
        // LlmContextOverflowException as the provider would. Verify enrichment at each
        // layer: provider supplies InputChars/ServerResponseBody, summarizer adds
        // SegmentIndex/TotalSegments, pipeline adds DocumentPath.
        var bed = CreatePipeline();

        // Multi-segment path: 3 segments so that the failure happens at segment index 1.
        bed.Chunker.SegmentDocument(Arg.Any<string>())
            .Returns((IReadOnlyList<string>)["seg-zero", "seg-one-fails", "seg-two"]);

        var llm = Substitute.For<ILlmClient>();
        llm.GenerateAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var prompt = ci.ArgAt<string>(1);
                if (prompt == "seg-zero")
                    return Task.FromResult("summary-zero");
                throw new LlmContextOverflowException(
                    "LLM rejected request (HTTP 400): too long")
                {
                    InputChars = 100,
                    ServerResponseBody = "too long",
                };
            });

        var summarizer = new DocumentSummarizer(llm);
        var contextualizer = new ChunkContextualizer(llm);
        var pipelineWithSummarizer = new IngestionPipeline(
            bed.Chunker, bed.Embedder, summarizer, contextualizer,
            ContextualizationLevel.PerChunk,
            bed.Repo, NullLogger<IngestionPipeline>.Instance);

        var doc = new Document(SourceId, "Title", "doc body");

        var ex = await Assert.ThrowsAsync<LlmContextOverflowException>(
            () => pipelineWithSummarizer.IngestAsync(CollectionName, doc, storedContentHash: null));

        // Provider-layer fields preserved end-to-end
        Assert.Equal(100, ex.InputChars);
        Assert.Equal("too long", ex.ServerResponseBody);
        // Summarizer-layer enrichment
        Assert.Equal(1, ex.SegmentIndex);
        Assert.Equal(3, ex.TotalSegments);
        // Pipeline-layer enrichment
        Assert.Equal(SourceId, ex.DocumentPath);
        // Composed message names each layer's context
        Assert.Contains("Ingestion failed", ex.Message);
        Assert.Contains("segment 2/3", ex.Message);
    }

    [Fact]
    public async Task IngestAsync_WithContextualizer_AppliesPrefixes()
    {
        var bed = CreatePipeline(ContextualizationLevel.PerChunk);

        // Multi-chunk path: contextualization (and the summarizer it depends on) only runs when
        // a doc produces more than one chunk. Override the default 1-chunk mock to return 2.
        bed.Chunker.Chunk(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => (IReadOnlyList<Chunk>)
            [
                MakeChunk(ci.ArgAt<string>(0), ci.ArgAt<string>(1), 0, ci.ArgAt<string>(2)),
                MakeChunk(ci.ArgAt<string>(0), ci.ArgAt<string>(1), 1, ci.ArgAt<string>(2)),
            ]);

        var doc = new Document(SourceId, "Title", "Content.");

        await bed.Pipeline.IngestAsync(CollectionName, doc, storedContentHash: null);

        await bed.Repo.Received(1).UpsertChunksAsync(
            CollectionName, SourceId,
            Arg.Is<IReadOnlyList<ChunkWithEmbedding>>(
                chunks => chunks.All(c => c.ContextualPrefix == "ctx")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_DocumentBrief_UsesSummaryAsEveryChunkPrefix()
    {
        var bed = CreatePipeline(ContextualizationLevel.DocumentBrief);
        bed.Chunker.Chunk(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => (IReadOnlyList<Chunk>)
            [
                MakeChunk(ci.ArgAt<string>(0), ci.ArgAt<string>(1), 0, ci.ArgAt<string>(2)),
                MakeChunk(ci.ArgAt<string>(0), ci.ArgAt<string>(1), 1, ci.ArgAt<string>(2)),
            ]);

        var doc = new Document(SourceId, "Title", "Content.");

        await bed.Pipeline.IngestAsync(CollectionName, doc, storedContentHash: null);

        await bed.Repo.Received(1).UpsertChunksAsync(
            CollectionName, SourceId,
            Arg.Is<IReadOnlyList<ChunkWithEmbedding>>(
                chunks => chunks.All(c => c.ContextualPrefix == "A summary.")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_Breadcrumb_UsesHeadingTrailAsPrefix()
    {
        var bed = CreatePipeline(ContextualizationLevel.Breadcrumb);
        bed.Chunker.Chunk(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns((IReadOnlyList<Chunk>)
            [
                MakeChunk(CollectionName, SourceId, 0, "# Jamaica\n\nIntro text."),
                MakeChunk(CollectionName, SourceId, 1, "## Government\n\nParliament details."),
            ]);

        var doc = new Document(SourceId, "Jamaica", "text");

        await bed.Pipeline.IngestAsync(CollectionName, doc, storedContentHash: null);

        await bed.Repo.Received(1).UpsertChunksAsync(
            CollectionName, SourceId,
            Arg.Is<IReadOnlyList<ChunkWithEmbedding>>(chunks =>
                chunks[0].ContextualPrefix == "Jamaica"
                && chunks[1].ContextualPrefix == "Jamaica > Government"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_Breadcrumb_NoHeadings_FallsBackToDocumentTitle()
    {
        var bed = CreatePipeline(ContextualizationLevel.Breadcrumb);
        bed.Chunker.Chunk(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns((IReadOnlyList<Chunk>)
            [
                MakeChunk(CollectionName, SourceId, 0, "Plain text, first part."),
                MakeChunk(CollectionName, SourceId, 1, "Plain text, second part."),
            ]);

        var doc = new Document(SourceId, "My Note", "text");

        await bed.Pipeline.IngestAsync(CollectionName, doc, storedContentHash: null);

        await bed.Repo.Received(1).UpsertChunksAsync(
            CollectionName, SourceId,
            Arg.Is<IReadOnlyList<ChunkWithEmbedding>>(
                chunks => chunks.All(c => c.ContextualPrefix == "My Note")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_DocumentBrief_LargeDocument_UsesOwnSegmentSummary()
    {
        var bed = CreatePipeline(ContextualizationLevel.DocumentBrief);
        bed.Chunker.SegmentDocument(Arg.Any<string>())
            .Returns((IReadOnlyList<string>)["seg-zero", "seg-one"]);
        bed.Summarizer!.SummarizeSegmentsAsync(
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<string>)ci.Arg<IReadOnlyList<string>>()
                .Select(s => $"summary of {s}").ToArray());

        var doc = new Document(SourceId, "Title", "text");

        await bed.Pipeline.IngestAsync(CollectionName, doc, storedContentHash: null);

        await bed.Repo.Received(1).UpsertChunksAsync(
            CollectionName, SourceId,
            Arg.Is<IReadOnlyList<ChunkWithEmbedding>>(chunks =>
                chunks.Count == 2
                && chunks[0].ContextualPrefix == "summary of seg-zero"
                && chunks[1].ContextualPrefix == "summary of seg-one"),
            Arg.Any<CancellationToken>());
    }
}
