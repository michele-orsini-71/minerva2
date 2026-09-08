using System.ClientModel;
using Minerva.Exceptions;
using Minerva.Providers;
using Minerva.Tests.TestSupport;
using OpenAI.Embeddings;

namespace Minerva.Tests.Providers;

[Trait("Category", "Providers")]
public class EmbeddingGenerationProviderTests
{
    // MaxRetryAttempts = 3 in the provider, so an exhausted retry loop makes 4 calls.
    private const int ExhaustedCallCount = 4;

    private static readonly string[] TwoInputs = ["a", "b"];

    [Fact]
    public async Task GenerateAsync_FacadeSucceeds_ReturnsNormalizedVectorsInInputOrder()
    {
        var facade = new FakeGenerationFacade(Embeddings([3f, 4f], [0f, 2f]));
        var provider = CreateProvider(facade);

        var result = await provider.GenerateAsync(TwoInputs);

        Assert.Equal(2, result.Count);
        AssertVector([0.6f, 0.8f], result[0].Vector.ToArray());
        AssertVector([0f, 1f], result[1].Vector.ToArray());
        Assert.Equal(TwoInputs, facade.LastInputs);
        Assert.Equal(1, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_EmptyInput_ReturnsEmptyWithoutCallingFacade()
    {
        var facade = new FakeGenerationFacade();
        var provider = CreateProvider(facade);

        var result = await provider.GenerateAsync([]);

        Assert.Empty(result);
        Assert.Equal(0, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_500ThenSuccess_RetriesOnce()
    {
        var facade = new FakeGenerationFacade(Http(500), Embeddings([1f, 0f], [0f, 1f]));
        var provider = CreateProvider(facade);

        var result = await provider.GenerateAsync(TwoInputs);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_429ThenSuccess_RetriesOnce()
    {
        var facade = new FakeGenerationFacade(Http(429), Embeddings([1f, 0f], [0f, 1f]));
        var provider = CreateProvider(facade);

        var result = await provider.GenerateAsync(TwoInputs);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_HttpRequestExceptionThenSuccess_RetriesOnce()
    {
        var facade = new FakeGenerationFacade(
            new HttpRequestException("connection reset"), Embeddings([1f, 0f], [0f, 1f]));
        var provider = CreateProvider(facade);

        var result = await provider.GenerateAsync(TwoInputs);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_500Exhausted_ThrowsEmbeddingExceptionAfterFourCalls()
    {
        var facade = new FakeGenerationFacade(Repeat(ExhaustedCallCount, () => Http(500)));
        var provider = CreateProvider(facade);

        var ex = await Assert.ThrowsAsync<EmbeddingException>(
            () => provider.GenerateAsync(TwoInputs));

        Assert.Contains("HTTP 500", ex.Message);
        Assert.IsType<ClientResultException>(ex.InnerException);
        Assert.Equal(ExhaustedCallCount, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_400_ThrowsEmbeddingExceptionWithoutRetry()
    {
        var facade = new FakeGenerationFacade(Http(400));
        var provider = CreateProvider(facade);

        var ex = await Assert.ThrowsAsync<EmbeddingException>(
            () => provider.GenerateAsync(TwoInputs));

        Assert.Contains("HTTP 400", ex.Message);
        Assert.Equal(1, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_FacadeCancelled_PropagatesOperationCanceled()
    {
        var facade = new FakeGenerationFacade(new OperationCanceledException());
        var provider = CreateProvider(facade);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GenerateAsync(TwoInputs));

        Assert.Equal(1, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_AfterFailure_RateLimiterSlotIsReleased()
    {
        var facade = new FakeGenerationFacade(Http(400), Embeddings([1f, 0f], [0f, 1f]));
        var provider = CreateProvider(facade);

        await Assert.ThrowsAsync<EmbeddingException>(() => provider.GenerateAsync(TwoInputs));

        // Concurrency is 1: if the slot leaked, this call would hang until the timeout.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await provider.GenerateAsync(TwoInputs, cancellationToken: timeout.Token);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task EmbedAsync_ReturnsOneArrayPerInput()
    {
        var facade = new FakeGenerationFacade(Embeddings([3f, 4f], [0f, 2f]));
        var provider = CreateProvider(facade);

        var result = await provider.EmbedAsync(TwoInputs);

        Assert.Equal(2, result.Count);
        AssertVector([0.6f, 0.8f], result[0]);
        AssertVector([0f, 1f], result[1]);
    }

    [Fact]
    public async Task PreflightAsync_ProbeFails_ReturnsEmbeddingFailureWithCause()
    {
        var probe = new StubProbeFacade(() => throw new HttpRequestException("refused"));
        var provider = CreateProvider(new FakeGenerationFacade(), probe);

        var failure = await provider.PreflightAsync();

        Assert.NotNull(failure);
        Assert.Equal("Embedding", failure.Stage);
        Assert.IsType<ProviderUnavailableException>(failure.Cause);
    }

    [Fact]
    public async Task PreflightAsync_ProbeSucceeds_ReturnsNull()
    {
        var provider = CreateProvider(new FakeGenerationFacade());

        var failure = await provider.PreflightAsync();

        Assert.Null(failure);
    }

    private static OpenAICompatibleEmbeddingProvider CreateProvider(
        IEmbeddingGenerationFacade generationFacade, IEmbeddingProbeFacade? probeFacade = null)
    {
        var endpoint = new Uri("http://localhost");
        var openAi = new OpenAI.OpenAIClient(
            new ApiKeyCredential("test"),
            new OpenAI.OpenAIClientOptions { Endpoint = endpoint });
        var embeddingClient = openAi.GetEmbeddingClient("test-model");
        var rateLimiter = new RateLimiter(concurrency: 1, requestsPerMinute: null);
        return new OpenAICompatibleEmbeddingProvider(
            embeddingClient, rateLimiter, "test-model", endpoint,
            probeFacade ?? new StubProbeFacade(() => 2),
            generationFacade,
            retryBaseDelay: TimeSpan.Zero);
    }

    private static ClientResultException Http(int status, string body = "") =>
        ClientResultExceptions.WithStatus(status, body);

    // OPENAI001: the model factory is marked experimental; it is the only way to build an embedding collection.
#pragma warning disable OPENAI001
    private static OpenAIEmbeddingCollection Embeddings(params float[][] vectors) =>
        OpenAIEmbeddingsModelFactory.OpenAIEmbeddingCollection(
            vectors.Select((v, i) => OpenAIEmbeddingsModelFactory.OpenAIEmbedding(i, v)),
            model: "test-model",
            usage: OpenAIEmbeddingsModelFactory.EmbeddingTokenUsage(1, 1));
#pragma warning restore OPENAI001

    private static object[] Repeat(int count, Func<object> factory) =>
        Enumerable.Range(0, count).Select(_ => factory()).ToArray();

    private static void AssertVector(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.InRange(actual[i], expected[i] - 0.001f, expected[i] + 0.001f);
    }

    // Each scripted step is either an OpenAIEmbeddingCollection to return or an Exception to throw.
    private sealed class FakeGenerationFacade : IEmbeddingGenerationFacade
    {
        private readonly Queue<object> _script;
        private int _callCount;

        public FakeGenerationFacade(params object[] script) => _script = new Queue<object>(script);

        public int CallCount => Volatile.Read(ref _callCount);
        public IList<string>? LastInputs { get; private set; }

        public Task<OpenAIEmbeddingCollection> GenerateEmbeddingsAsync(IList<string> inputs, CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            LastInputs = inputs;

            if (_script.Count == 0)
                throw new InvalidOperationException("FakeGenerationFacade script exhausted: unexpected extra call.");

            return _script.Dequeue() switch
            {
                OpenAIEmbeddingCollection embeddings => Task.FromResult(embeddings),
                Exception ex => Task.FromException<OpenAIEmbeddingCollection>(ex),
                var other => throw new InvalidOperationException($"Unsupported script step: {other.GetType().Name}"),
            };
        }
    }

    private sealed class StubProbeFacade : IEmbeddingProbeFacade
    {
        private readonly Func<int> _probe;

        public StubProbeFacade(Func<int> probe) => _probe = probe;

        public Task<int> EmbedAndCountDimensionsAsync(string input, CancellationToken ct) =>
            Task.FromResult(_probe());
    }
}
