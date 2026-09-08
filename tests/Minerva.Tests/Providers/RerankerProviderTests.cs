using System.Net;
using System.Text;
using System.Text.Json;
using Minerva.Providers;

namespace Minerva.Tests.Providers;

[Trait("Category", "Providers")]
public class RerankerProviderTests
{
    // MaxRetryAttempts = 3 in the provider, so an exhausted retry loop makes 4 calls.
    private const int ExhaustedCallCount = 4;
    private const string ModelId = "test-reranker";

    private static readonly List<string> ThreeTexts = ["first", "second", "third"];

    [Fact]
    public async Task RankTexts_ServerResponds_PlacesScoresByIndex()
    {
        var handler = new FakeHttpHandler(RerankOk(ModelId, (2, 0.9f), (0, 0.1f), (1, 0.5f)));
        var provider = CreateProvider(handler);

        var scores = await provider.RankTexts("q", ThreeTexts, CancellationToken.None);

        Assert.Equal([0.1f, 0.5f, 0.9f], scores);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RankTexts_PostsModelQueryAndDocumentsToRerankPath()
    {
        var handler = new FakeHttpHandler(RerankOk(ModelId, (0, 1f), (1, 0f), (2, 0f)));
        var provider = CreateProvider(handler);

        await provider.RankTexts("what is it", ThreeTexts, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal("/rerank", handler.LastRequest.RequestUri!.AbsolutePath);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(ModelId, body.RootElement.GetProperty("model").GetString());
        Assert.Equal("what is it", body.RootElement.GetProperty("query").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("documents").GetArrayLength());
    }

    [Fact]
    public async Task RankTexts_503ThenSuccess_RetriesOnce()
    {
        var handler = new FakeHttpHandler(
            Status(HttpStatusCode.ServiceUnavailable), RerankOk(ModelId, (0, 1f), (1, 0f), (2, 0f)));
        var provider = CreateProvider(handler);

        var scores = await provider.RankTexts("q", ThreeTexts, CancellationToken.None);

        Assert.Equal(3, scores.Length);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task RankTexts_429ThenSuccess_RetriesOnce()
    {
        var handler = new FakeHttpHandler(
            Status(HttpStatusCode.TooManyRequests), RerankOk(ModelId, (0, 1f), (1, 0f), (2, 0f)));
        var provider = CreateProvider(handler);

        var scores = await provider.RankTexts("q", ThreeTexts, CancellationToken.None);

        Assert.Equal(3, scores.Length);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task RankTexts_ConnectionFailureThenSuccess_RetriesOnce()
    {
        // A refused connection surfaces as HttpRequestException with no status code.
        var handler = new FakeHttpHandler(
            new HttpRequestException("connection refused"), RerankOk(ModelId, (0, 1f), (1, 0f), (2, 0f)));
        var provider = CreateProvider(handler);

        var scores = await provider.RankTexts("q", ThreeTexts, CancellationToken.None);

        Assert.Equal(3, scores.Length);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task RankTexts_TimeoutThenSuccess_RetriesOnce()
    {
        // HttpClient reports its own timeout as TaskCanceledException wrapping TimeoutException.
        var handler = new FakeHttpHandler(
            new TaskCanceledException("timed out", new TimeoutException()),
            RerankOk(ModelId, (0, 1f), (1, 0f), (2, 0f)));
        var provider = CreateProvider(handler);

        var scores = await provider.RankTexts("q", ThreeTexts, CancellationToken.None);

        Assert.Equal(3, scores.Length);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task RankTexts_503Exhausted_ThrowsHttpRequestExceptionAfterFourCalls()
    {
        var handler = new FakeHttpHandler(Repeat(ExhaustedCallCount, () => Status(HttpStatusCode.ServiceUnavailable)));
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.RankTexts("q", ThreeTexts, CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Equal(ExhaustedCallCount, handler.CallCount);
    }

    [Fact]
    public async Task RankTexts_400_ThrowsHttpRequestExceptionWithoutRetry()
    {
        var handler = new FakeHttpHandler(Status(HttpStatusCode.BadRequest));
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.RankTexts("q", ThreeTexts, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RankTexts_NullJsonBody_ThrowsInvalidOperationWithoutRetry()
    {
        var handler = new FakeHttpHandler(Json("null"));
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.RankTexts("q", ThreeTexts, CancellationToken.None));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_ServedModelMatches_Completes()
    {
        var handler = new FakeHttpHandler(RerankOk(ModelId, (0, 1f), (1, 0f), (2, 0f)));
        var provider = CreateProvider(handler);

        await provider.CheckAvailabilityAsync();

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_ServedModelDiffers_ThrowsInvalidOperation()
    {
        var handler = new FakeHttpHandler(RerankOk("other-model", (0, 1f), (1, 0f), (2, 0f)));
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CheckAvailabilityAsync());

        Assert.Contains("other-model", ex.Message);
        Assert.Contains(ModelId, ex.Message);
    }

    [Fact]
    public async Task PreflightAsync_ServedModelDiffers_ReturnsFailureWithCause()
    {
        var handler = new FakeHttpHandler(RerankOk("other-model", (0, 1f), (1, 0f), (2, 0f)));
        var provider = CreateProvider(handler);

        var failure = await provider.PreflightAsync();

        Assert.NotNull(failure);
        Assert.Equal("Reranker", failure.Stage);
        Assert.IsType<InvalidOperationException>(failure.Cause);
    }

    [Fact]
    public async Task PreflightAsync_ServerUnreachable_ReturnsFailureWithCause()
    {
        var handler = new FakeHttpHandler(new HttpRequestException("connection refused"));
        var provider = CreateProvider(handler);

        var failure = await provider.PreflightAsync();

        Assert.NotNull(failure);
        Assert.Equal("Reranker", failure.Stage);
        Assert.IsType<HttpRequestException>(failure.Cause);
    }

    [Fact]
    public async Task PreflightAsync_ServedModelMatches_ReturnsNull()
    {
        var handler = new FakeHttpHandler(RerankOk(ModelId, (0, 1f), (1, 0f), (2, 0f)));
        var provider = CreateProvider(handler);

        var failure = await provider.PreflightAsync();

        Assert.Null(failure);
    }

    private static HttpRerankerProvider CreateProvider(FakeHttpHandler handler) =>
        new(ModelId, new Uri("http://localhost:9932"), handler, retryBaseDelay: TimeSpan.Zero);

    private static HttpResponseMessage RerankOk(string model, params (int index, float score)[] results)
    {
        var payload = new
        {
            model,
            @object = "list",
            usage = new { },
            results = results.Select(r => new { index = r.index, relevance_score = r.score }),
        };
        return Json(JsonSerializer.Serialize(payload));
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    private static object[] Repeat(int count, Func<object> factory) =>
        Enumerable.Range(0, count).Select(_ => factory()).ToArray();

    // Each scripted step is either an HttpResponseMessage to return or an Exception to throw.
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Queue<object> _script;
        private int _callCount;

        public FakeHttpHandler(params object[] script) => _script = new Queue<object>(script);

        public int CallCount => Volatile.Read(ref _callCount);
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_script.Count == 0)
                throw new InvalidOperationException("FakeHttpHandler script exhausted: unexpected extra call.");

            return _script.Dequeue() switch
            {
                HttpResponseMessage response => response,
                Exception ex => throw ex,
                var other => throw new InvalidOperationException($"Unsupported script step: {other.GetType().Name}"),
            };
        }
    }
}
