using System.ClientModel;
using Minerva.Exceptions;
using Minerva.Providers;
using Minerva.Tests.TestSupport;
using OpenAI.Chat;

namespace Minerva.Tests.Providers;

[Trait("Category", "Providers")]
public class LlmProviderTests
{
    // MaxRetryAttempts = 5 in the provider, so an exhausted retry loop makes 6 calls.
    private const int ExhaustedCallCount = 6;

    [Fact]
    public async Task GenerateAsync_FacadeSucceeds_ReturnsTextAndCallsOnce()
    {
        var facade = new FakeChatFacade(Completion("hi"));
        var provider = CreateProvider(facade);

        var text = await provider.GenerateAsync(null, "hello");

        Assert.Equal("hi", text);
        Assert.Equal(1, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_500ThenSuccess_RetriesOnce()
    {
        var facade = new FakeChatFacade(Http(500), Completion("hi"));
        var provider = CreateProvider(facade);

        var text = await provider.GenerateAsync(null, "hello");

        Assert.Equal("hi", text);
        Assert.Equal(2, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_429ThenSuccess_RetriesOnce()
    {
        var facade = new FakeChatFacade(Http(429), Completion("hi"));
        var provider = CreateProvider(facade);

        var text = await provider.GenerateAsync(null, "hello");

        Assert.Equal("hi", text);
        Assert.Equal(2, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_HttpRequestExceptionThenSuccess_RetriesOnce()
    {
        var facade = new FakeChatFacade(new HttpRequestException("connection reset"), Completion("hi"));
        var provider = CreateProvider(facade);

        var text = await provider.GenerateAsync(null, "hello");

        Assert.Equal("hi", text);
        Assert.Equal(2, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_500Exhausted_ThrowsProviderUnavailableAfterSixCalls()
    {
        var facade = new FakeChatFacade(Repeat(ExhaustedCallCount, () => Http(500, "upstream down")));
        var provider = CreateProvider(facade);

        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(
            () => provider.GenerateAsync(null, "hello"));

        Assert.Contains("HTTP 500", ex.Message);
        Assert.Contains("upstream down", ex.Message);
        Assert.IsType<ClientResultException>(ex.InnerException);
        Assert.Equal(ExhaustedCallCount, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_Plain400_ThrowsContextOverflowWithoutRetry()
    {
        var facade = new FakeChatFacade(Http(400, "context length exceeded"));
        var provider = CreateProvider(facade);

        var ex = await Assert.ThrowsAsync<LlmContextOverflowException>(
            () => provider.GenerateAsync(null, "hello"));

        Assert.Equal(1, facade.CallCount);
        Assert.Equal("context length exceeded", ex.ServerResponseBody);
        Assert.Equal("hello".Length, ex.InputChars);
    }

    [Fact]
    public async Task GenerateAsync_400WithUnloadedBody_IsRetriedAndWrappedAsUnavailable()
    {
        var facade = new FakeChatFacade(Repeat(ExhaustedCallCount, () => Http(400, "model was unloaded")));
        var provider = CreateProvider(facade);

        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(
            () => provider.GenerateAsync(null, "hello"));

        Assert.IsNotType<LlmContextOverflowException>(ex);
        Assert.Contains("Model was unloaded", ex.Message);
        Assert.Equal(ExhaustedCallCount, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_401_ThrowsProviderUnavailableWithoutRetry()
    {
        var facade = new FakeChatFacade(Http(401, "bad key"));
        var provider = CreateProvider(facade);

        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(
            () => provider.GenerateAsync(null, "hello"));

        Assert.Contains("HTTP 401", ex.Message);
        Assert.Equal(1, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_FacadeCancelled_PropagatesOperationCanceled()
    {
        var facade = new FakeChatFacade(new OperationCanceledException());
        var provider = CreateProvider(facade);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GenerateAsync(null, "hello"));

        Assert.Equal(1, facade.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_AfterFailure_RateLimiterSlotIsReleased()
    {
        var facade = new FakeChatFacade(Http(401), Completion("hi"));
        var provider = CreateProvider(facade);

        await Assert.ThrowsAsync<ProviderUnavailableException>(
            () => provider.GenerateAsync(null, "hello"));

        // Concurrency is 1: if the slot leaked, this call would hang until the timeout.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var text = await provider.GenerateAsync(null, "hello", ct: timeout.Token);

        Assert.Equal("hi", text);
    }

    [Fact]
    public async Task GenerateAsync_WithSystemPrompt_MapsSystemThenUserMessage()
    {
        var facade = new FakeChatFacade(Completion("hi"));
        var provider = CreateProvider(facade);

        await provider.GenerateAsync("sys", "user");

        Assert.NotNull(facade.LastMessages);
        Assert.Equal(2, facade.LastMessages.Count);
        Assert.IsType<SystemChatMessage>(facade.LastMessages[0]);
        Assert.IsType<UserChatMessage>(facade.LastMessages[1]);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_ServedModelDiffers_ThrowsInvalidOperation()
    {
        var facade = new FakeChatFacade(Completion("pong", model: "other-model"));
        var provider = CreateProvider(facade);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CheckAvailabilityAsync());

        Assert.Contains("other-model", ex.Message);
        Assert.Contains("test-model", ex.Message);
    }

    [Fact]
    public async Task PreflightAsync_ServedModelDiffers_ReturnsLlmFailureWithCause()
    {
        var facade = new FakeChatFacade(Completion("pong", model: "other-model"));
        var provider = CreateProvider(facade);

        var failure = await provider.PreflightAsync();

        Assert.NotNull(failure);
        Assert.Equal("Llm", failure.Stage);
        Assert.IsType<InvalidOperationException>(failure.Cause);
    }

    [Fact]
    public async Task PreflightAsync_ModelMatches_ReturnsNull()
    {
        var facade = new FakeChatFacade(Completion("pong"));
        var provider = CreateProvider(facade);

        var failure = await provider.PreflightAsync();

        Assert.Null(failure);
    }

    private static OpenAICompatibleLlmProvider CreateProvider(IChatClientFacade facade)
    {
        var endpoint = new Uri("http://localhost");
        var openAi = new OpenAI.OpenAIClient(
            new ApiKeyCredential("test"),
            new OpenAI.OpenAIClientOptions { Endpoint = endpoint });
        var chatClient = openAi.GetChatClient("test-model");
        var rateLimiter = new RateLimiter(concurrency: 1, requestsPerMinute: null);
        return new OpenAICompatibleLlmProvider(
            chatClient, rateLimiter, "test-model", endpoint, facade, retryBaseDelay: TimeSpan.Zero);
    }

    private static ClientResultException Http(int status, string body = "") =>
        ClientResultExceptions.WithStatus(status, body);

    // OPENAI001: the model factory is marked experimental; it is the only way to build a ChatCompletion.
#pragma warning disable OPENAI001
    private static ChatCompletion Completion(string text, string model = "test-model") =>
        OpenAIChatModelFactory.ChatCompletion(
            role: ChatMessageRole.Assistant,
            content: new ChatMessageContent(text),
            model: model,
            serviceTier: null);
#pragma warning restore OPENAI001

    private static object[] Repeat(int count, Func<object> factory) =>
        Enumerable.Range(0, count).Select(_ => factory()).ToArray();

    // Each scripted step is either a ChatCompletion to return or an Exception to throw.
    private sealed class FakeChatFacade : IChatClientFacade
    {
        private readonly Queue<object> _script;
        private int _callCount;

        public FakeChatFacade(params object[] script) => _script = new Queue<object>(script);

        public int CallCount => Volatile.Read(ref _callCount);
        public IList<ChatMessage>? LastMessages { get; private set; }

        public Task<ChatCompletion> CompleteChatAsync(
            IList<ChatMessage> messages, ChatCompletionOptions options, CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            LastMessages = messages;

            if (_script.Count == 0)
                throw new InvalidOperationException("FakeChatFacade script exhausted: unexpected extra call.");

            return _script.Dequeue() switch
            {
                ChatCompletion completion => Task.FromResult(completion),
                Exception ex => Task.FromException<ChatCompletion>(ex),
                var other => throw new InvalidOperationException($"Unsupported script step: {other.GetType().Name}"),
            };
        }
    }
}
