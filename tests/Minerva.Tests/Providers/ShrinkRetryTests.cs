using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Providers;

namespace Minerva.Tests.Providers;

[Trait("Category", "Providers")]
public class ShrinkRetryTests
{
    private static readonly Uri BaseEndpoint = new("http://localhost:1234/v1");

    [Fact]
    public async Task GetResponse_FirstCallReturns400_HalvesLastUserMessageAndRetries()
    {
        var first = OverflowResponse("first call: too long");
        var facade = new ScriptedFacade(
            ThrowOverflow(first),
            CompleteWith("ok"));
        var provider = BuildProvider(facade);

        var messages = new[]
        {
            new ChatMessage(ChatRole.System, "system"),
            new ChatMessage(ChatRole.User, "abcdefghij"), // 10 chars
        };

        var response = await provider.GetResponseAsync(messages);

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, facade.Calls.Count);
        // Second call halves the last user message: 10 → 5 chars
        var secondLastUser = facade.Calls[1].Messages.Last(m => m is OpenAI.Chat.UserChatMessage);
        Assert.Equal("abcde", JoinUserText(secondLastUser));
        // System message is preserved
        Assert.Equal(2, facade.Calls[1].Messages.Count);
    }

    [Fact]
    public async Task GetResponse_BothCalls400_ThrowsLlmContextOverflowException()
    {
        var first = OverflowResponse("first 400");
        var second = OverflowResponse("second 400");
        var facade = new ScriptedFacade(
            ThrowOverflow(first),
            ThrowOverflow(second));
        var provider = BuildProvider(facade);

        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "abcdefghij"),
        };

        var ex = await Assert.ThrowsAsync<LlmContextOverflowException>(
            () => provider.GetResponseAsync(messages));

        Assert.Equal(10, ex.InputChars);
        Assert.Contains("second 400", ex.ServerResponseBody);
        Assert.Equal(2, facade.Calls.Count);
    }

    [Fact]
    public async Task GetResponse_FirstCallSucceeds_NoRetry()
    {
        var facade = new ScriptedFacade(CompleteWith("done"));
        var provider = BuildProvider(facade);

        var response = await provider.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hi") });

        Assert.Equal("done", response.Text);
        Assert.Single(facade.Calls);
    }

    [Fact]
    public async Task GetResponse_400AndUserMessageTooShortToHalve_ThrowsWithoutRetry()
    {
        var first = OverflowResponse("too long");
        var facade = new ScriptedFacade(ThrowOverflow(first));
        var provider = BuildProvider(facade);

        // 1-char user message: halving would yield empty, so we don't retry.
        var messages = new[] { new ChatMessage(ChatRole.User, "x") };

        var ex = await Assert.ThrowsAsync<LlmContextOverflowException>(
            () => provider.GetResponseAsync(messages));

        Assert.Equal(1, ex.InputChars);
        Assert.Contains("too long", ex.ServerResponseBody);
        Assert.Single(facade.Calls);
    }

    [Fact]
    public async Task GetResponse_NonShrinkable400_StillTriesShrinkOnce_ThenThrows()
    {
        // 400 from a malformed prompt: shrink-retry fires anyway, fails again, throws typed exception.
        var facade = new ScriptedFacade(
            ThrowOverflow(OverflowResponse("malformed")),
            ThrowOverflow(OverflowResponse("still malformed")));
        var provider = BuildProvider(facade);

        var ex = await Assert.ThrowsAsync<LlmContextOverflowException>(
            () => provider.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "abcdef") }));

        Assert.Equal(6, ex.InputChars);
        Assert.Equal(2, facade.Calls.Count);
    }

    [Fact]
    public async Task GetResponse_500_DoesNotTriggerShrinkRetry()
    {
        var facade = new ScriptedFacade(ThrowStatus(500, "boom"));
        var provider = BuildProvider(facade);

        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(
            () => provider.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "anything") }));

        Assert.IsNotType<LlmContextOverflowException>(ex);
        // 500 is retried by the resilience pipeline (3 attempts), but never shrink-retried.
        // We don't assert call count strictly here; the type assertion is the contract.
    }

    private static OpenAICompatibleLlmProvider BuildProvider(IChatClientFacade facade)
    {
        var openAIClient = new OpenAI.OpenAIClient(
            new ApiKeyCredential("no-key"),
            new OpenAI.OpenAIClientOptions { Endpoint = BaseEndpoint });
        var chatClient = openAIClient.GetChatClient("test-model");
        var rateLimiter = new RateLimiter(concurrency: 1, requestsPerMinute: null);
        return new OpenAICompatibleLlmProvider(
            chatClient,
            rateLimiter,
            "test-model",
            BaseEndpoint,
            ContextLengthProbe.None,
            chatFacade: facade,
            probeHandler: null);
    }

    private static string JoinUserText(OpenAI.Chat.ChatMessage m)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var part in m.Content)
            sb.Append(part.Text ?? string.Empty);
        return sb.ToString();
    }

    private static FakePipelineResponse OverflowResponse(string body) =>
        new FakePipelineResponse(400, body);

    private static Func<IList<OpenAI.Chat.ChatMessage>, OpenAI.Chat.ChatCompletion> CompleteWith(string text) =>
        _ =>
        {
#pragma warning disable OPENAI001
            return OpenAI.Chat.OpenAIChatModelFactory.ChatCompletion(
                model: "test-model",
                content: new OpenAI.Chat.ChatMessageContent(text));
#pragma warning restore OPENAI001
        };

    private static Func<IList<OpenAI.Chat.ChatMessage>, OpenAI.Chat.ChatCompletion> ThrowOverflow(
        FakePipelineResponse response) =>
        _ => throw new ClientResultException(response);

    private static Func<IList<OpenAI.Chat.ChatMessage>, OpenAI.Chat.ChatCompletion> ThrowStatus(
        int status, string body) =>
        _ => throw new ClientResultException(new FakePipelineResponse(status, body));

    private sealed class ScriptedFacade : IChatClientFacade
    {
        private readonly Queue<Func<IList<OpenAI.Chat.ChatMessage>, OpenAI.Chat.ChatCompletion>> _scripts;
        public List<RecordedCall> Calls { get; } = new();

        public ScriptedFacade(params Func<IList<OpenAI.Chat.ChatMessage>, OpenAI.Chat.ChatCompletion>[] scripts)
        {
            _scripts = new Queue<Func<IList<OpenAI.Chat.ChatMessage>, OpenAI.Chat.ChatCompletion>>(scripts);
        }

        public Task<OpenAI.Chat.ChatCompletion> CompleteChatAsync(
            IList<OpenAI.Chat.ChatMessage> messages,
            OpenAI.Chat.ChatCompletionOptions options,
            CancellationToken ct)
        {
            var snapshot = new List<OpenAI.Chat.ChatMessage>(messages);
            Calls.Add(new RecordedCall(snapshot));
            if (_scripts.Count == 0)
                throw new InvalidOperationException("ScriptedFacade ran out of scripted responses");
            var script = _scripts.Dequeue();
            return Task.FromResult(script(messages));
        }
    }

    private sealed record RecordedCall(IList<OpenAI.Chat.ChatMessage> Messages);

    private sealed class FakePipelineResponse : PipelineResponse
    {
        private readonly int _status;
        private readonly string _body;
        private BinaryData _content;

        public FakePipelineResponse(int status, string body)
        {
            _status = status;
            _body = body;
            _content = BinaryData.FromString(body);
        }

        public override int Status => _status;
        public override string ReasonPhrase => string.Empty;
        public override Stream? ContentStream
        {
            get => _content.ToStream();
            set => _content = value is null ? BinaryData.FromString(string.Empty) : BinaryData.FromStream(value);
        }
        public override BinaryData Content => _content;
        protected override PipelineResponseHeaders HeadersCore { get; } = new EmptyHeaders();
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => _content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_content);
        public override void Dispose() { }

        private sealed class EmptyHeaders : PipelineResponseHeaders
        {
            public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
                Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
            public override bool TryGetValue(string name, out string? value)
            {
                value = null;
                return false;
            }
            public override bool TryGetValues(string name, out IEnumerable<string>? values)
            {
                values = null;
                return false;
            }
        }
    }
}
