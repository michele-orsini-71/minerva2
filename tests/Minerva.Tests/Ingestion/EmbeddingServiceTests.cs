using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Ingestion;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Minerva.Tests.Ingestion;

[Trait("Category", "Ingestion")]
public class EmbeddingServiceTests
{
    private static readonly float[] SampleVector = [0.1f, 0.2f, 0.3f];

    private static IEmbeddingClient CreateMockClient()
    {
        var client = Substitute.For<IEmbeddingClient>();

        client.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var texts = callInfo.Arg<IReadOnlyList<string>>();
                return Task.FromResult(
                    (IReadOnlyList<float[]>)texts.Select(_ => SampleVector).ToArray());
            });

        return client;
    }

    [Fact]
    public async Task EmbedAsync_EmptyInput_ReturnsEmpty()
    {
        var client = CreateMockClient();
        var service = new EmbeddingService(client, batchSize: 10,
            NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync([]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task EmbedAsync_BatchesCorrectly()
    {
        var client = CreateMockClient();
        var service = new EmbeddingService(client, batchSize: 2,
            NullLogger<EmbeddingService>.Instance);

        var texts = new[] { "a", "b", "c", "d", "e" };
        var result = await service.EmbedAsync(texts);

        Assert.Equal(5, result.Count);

        // Should have been called 3 times: [a,b], [c,d], [e]
        await client.Received(3).EmbedAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmbedAsync_FallsBackToIndividualOnBatchFailure()
    {
        var client = Substitute.For<IEmbeddingClient>();
        var callCount = 0;

        client.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var texts = callInfo.Arg<IReadOnlyList<string>>();
                callCount++;

                // First call (batch of 3) fails, subsequent individual calls succeed
                if (texts.Count > 1)
                    throw new InvalidOperationException("Batch failed");

                return Task.FromResult((IReadOnlyList<float[]>)new[] { SampleVector });
            });

        var service = new EmbeddingService(client, batchSize: 3,
            NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync(new[] { "a", "b", "c" });

        Assert.Equal(3, result.Count);
        // 1 failed batch + 3 individual = 4 calls
        Assert.Equal(4, callCount);
    }

    [Fact]
    public async Task EmbedAsync_ReportsProgressAccurately()
    {
        var client = CreateMockClient();
        var service = new EmbeddingService(client, batchSize: 2,
            NullLogger<EmbeddingService>.Instance);

        var progressValues = new List<int>();
        var progress = new Progress<int>(v => progressValues.Add(v));

        await service.EmbedAsync(new[] { "a", "b", "c" }, progress);

        // Allow Progress<T> callbacks to flush (they run on the thread pool)
        await Task.Delay(50);

        Assert.Contains(3, progressValues); // Final progress should be total count
    }

    [Fact]
    public async Task EmbedAsync_PropagatesCancellation()
    {
        var client = Substitute.For<IEmbeddingClient>();

        client.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync<OperationCanceledException>();

        var service = new EmbeddingService(client, batchSize: 10,
            NullLogger<EmbeddingService>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.EmbedAsync(new[] { "a" }));
    }
}
