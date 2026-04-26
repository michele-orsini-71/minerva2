using System.ClientModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minerva.Configuration;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Providers;
using Minerva.Readiness.Checks;
using OAI = OpenAI.Chat;

namespace Minerva.Tests.Readiness.Checks;

[Trait("Category", "Readiness")]
public class LlmCallCheckTests
{
    [Fact]
    public async Task ShortCircuits_WhenLlmNull()
    {
        var check = Build(llm: null, probe: null);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.LLM.NOT_CONFIGURED", result.Code);
    }

    [Fact]
    public async Task Passes_WhenProbeSucceeds()
    {
        var check = Build(SomeLlm(), new FakeProbe(_ => Task.CompletedTask));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.LLM.OK", result.Code);
    }

    [Fact]
    public async Task Fails_WhenProbeThrows()
    {
        var check = Build(SomeLlm(),
            new FakeProbe(_ => throw new HttpRequestException("connection refused Bearer abcdef")));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.LLM.UNAVAILABLE", result.Code);
        Assert.Contains("http://llm.example", result.Remediation);
        Assert.Contains("llama3", result.Remediation);
        Assert.NotNull(result.Message);
        Assert.DoesNotContain("abcdef", result.Message);
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var check = Build(SomeLlm(),
            new FakeProbe(ct => Task.FromCanceled(ct)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => check.RunAsync(cts.Token));
    }

    [Fact]
    public async Task CheckAvailabilityAsync_TreatsHttp400_AsReachable()
    {
        // The OpenAICompatibleLlmProvider's CheckAvailabilityAsync swallows a 400 response
        // because reasoning models reject `MaxOutputTokenCount = 5` but are still reachable.
        var rateLimiter = new RateLimiter(concurrency: 1, requestsPerMinute: null);
        var endpoint = new Uri("http://llm.example");
        var openAi = new OpenAI.OpenAIClient(
            new ApiKeyCredential("test"),
            new OpenAI.OpenAIClientOptions { Endpoint = endpoint });
        var sdkChat = openAi.GetChatClient("llama3");
        var facade = new ThrowingChatFacade(new ClientResultException(
            "max_output_tokens not supported",
            new FakePipelineResponse(400),
            innerException: null!));
        var provider = new OpenAICompatibleLlmProvider(sdkChat, rateLimiter, "llama3", endpoint, facade);

        await provider.CheckAvailabilityAsync(CancellationToken.None);
        // No throw == reachable. (Asserted by reaching this line.)
    }

    [Fact]
    public async Task CheckAvailabilityAsync_PropagatesNon400Errors()
    {
        var rateLimiter = new RateLimiter(concurrency: 1, requestsPerMinute: null);
        var endpoint = new Uri("http://llm.example");
        var openAi = new OpenAI.OpenAIClient(
            new ApiKeyCredential("test"),
            new OpenAI.OpenAIClientOptions { Endpoint = endpoint });
        var sdkChat = openAi.GetChatClient("llama3");
        var facade = new ThrowingChatFacade(new HttpRequestException("connection refused"));
        var provider = new OpenAICompatibleLlmProvider(sdkChat, rateLimiter, "llama3", endpoint, facade);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.CheckAvailabilityAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CheckAvailabilityAsync_DoesNotRetry_PollyBypass()
    {
        // A transient HttpRequestException would normally be retried 3 more times
        // by the resilience pipeline. The probe bypasses Polly so it must be called once.
        var rateLimiter = new RateLimiter(concurrency: 1, requestsPerMinute: null);
        var endpoint = new Uri("http://llm.example");
        var openAi = new OpenAI.OpenAIClient(
            new ApiKeyCredential("test"),
            new OpenAI.OpenAIClientOptions { Endpoint = endpoint });
        var sdkChat = openAi.GetChatClient("llama3");
        var counting = new CountingChatFacade(new HttpRequestException("transient"));
        var provider = new OpenAICompatibleLlmProvider(sdkChat, rateLimiter, "llama3", endpoint, counting);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.CheckAvailabilityAsync(CancellationToken.None));

        Assert.Equal(1, counting.CallCount);
    }

    private static ProviderOptions SomeLlm() => new()
    {
        BaseUrl = "http://llm.example",
        Model = "llama3",
    };

    private static LlmCallCheck Build(ProviderOptions? llm, ILlmAvailabilityProbe? probe)
    {
        var options = Options.Create(new MinervaOptions { Llm = llm });
        var services = new ServiceCollection();
        if (probe is not null)
            services.AddSingleton(probe);
        var sp = services.BuildServiceProvider();
        return new LlmCallCheck(options, sp, NullLogger<LlmCallCheck>.Instance);
    }

    private sealed class FakeProbe : ILlmAvailabilityProbe
    {
        private readonly Func<CancellationToken, Task> _impl;
        public FakeProbe(Func<CancellationToken, Task> impl) => _impl = impl;
        public Task CheckAvailabilityAsync(CancellationToken ct = default) => _impl(ct);
    }

    private sealed class ThrowingChatFacade : IChatClientFacade
    {
        private readonly Exception _toThrow;
        public ThrowingChatFacade(Exception toThrow) => _toThrow = toThrow;
        public Task CompleteChatAsync(
            IList<OAI.ChatMessage> messages,
            OAI.ChatCompletionOptions options,
            CancellationToken ct) => throw _toThrow;
    }

    private sealed class CountingChatFacade : IChatClientFacade
    {
        private readonly Exception _toThrow;
        private int _count;
        public int CallCount => Volatile.Read(ref _count);
        public CountingChatFacade(Exception toThrow) => _toThrow = toThrow;
        public Task CompleteChatAsync(
            IList<OAI.ChatMessage> messages,
            OAI.ChatCompletionOptions options,
            CancellationToken ct)
        {
            Interlocked.Increment(ref _count);
            throw _toThrow;
        }
    }

    private sealed class FakePipelineResponse : System.ClientModel.Primitives.PipelineResponse
    {
        public FakePipelineResponse(int status) => Status = status;
        public override int Status { get; }
        public override string ReasonPhrase => "Bad Request";
        public override Stream? ContentStream { get => null; set { } }
        public override BinaryData Content => BinaryData.FromString(string.Empty);
        protected override System.ClientModel.Primitives.PipelineResponseHeaders HeadersCore { get; } = new EmptyHeaders();
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Content);
        public override void Dispose() { }

        private sealed class EmptyHeaders : System.ClientModel.Primitives.PipelineResponseHeaders
        {
            public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
                Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
            public override bool TryGetValue(string name, out string? value) { value = null; return false; }
            public override bool TryGetValues(string name, out IEnumerable<string>? values) { values = null; return false; }
        }
    }
}
