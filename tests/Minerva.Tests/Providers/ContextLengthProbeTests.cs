using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Models;
using Minerva.Providers;

namespace Minerva.Tests.Providers;

[Trait("Category", "Providers")]
public class ContextLengthProbeTests
{
    private static readonly Uri BaseEndpoint = new("http://localhost:1234/v1");

    [Fact]
    public async Task Probe_None_ReturnsNullWithoutHttpCall()
    {
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.NotFound, ""));
        var provider = BuildProvider(ContextLengthProbe.None, handler);

        int? result = await provider.ProbeLoadedContextLengthAsync();

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Probe_LMStudio_ReturnsLoadedContextLengthForMatchingModel()
    {
        const string body = """
            {
              "data": [
                { "id": "other-model", "loaded_context_length": 8192 },
                { "id": "test-model",  "loaded_context_length": 4096, "max_context_length": 32768 }
              ]
            }
            """;
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.OK, body));
        var provider = BuildProvider(ContextLengthProbe.LMStudio, handler, modelId: "test-model");

        int? result = await provider.ProbeLoadedContextLengthAsync();

        Assert.Equal(4096, result);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("http://localhost:1234/api/v0/models", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Probe_LMStudio_NoMatchingModel_ReturnsNull()
    {
        const string body = """
            { "data": [ { "id": "other-model", "loaded_context_length": 8192 } ] }
            """;
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.OK, body));
        var provider = BuildProvider(ContextLengthProbe.LMStudio, handler, modelId: "test-model");

        int? result = await provider.ProbeLoadedContextLengthAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task Probe_LMStudio_OnlyMaxContextLength_ReturnsNull()
    {
        const string body = """
            { "data": [ { "id": "test-model", "max_context_length": 32768 } ] }
            """;
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.OK, body));
        var provider = BuildProvider(ContextLengthProbe.LMStudio, handler, modelId: "test-model");

        int? result = await provider.ProbeLoadedContextLengthAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task Probe_LMStudio_404_ReturnsNull()
    {
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.NotFound, ""));
        var provider = BuildProvider(ContextLengthProbe.LMStudio, handler);

        int? result = await provider.ProbeLoadedContextLengthAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task Probe_LMStudio_MalformedJson_ReturnsNull()
    {
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.OK, "{not-json"));
        var provider = BuildProvider(ContextLengthProbe.LMStudio, handler);

        int? result = await provider.ProbeLoadedContextLengthAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task Probe_Ollama_ParsesNumCtxFromParametersString()
    {
        const string body = """
            {
              "modelfile": "...",
              "parameters": "stop <|eot|>\nnum_ctx 8192\ntemperature 0.7"
            }
            """;
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.OK, body));
        var provider = BuildProvider(ContextLengthProbe.Ollama, handler, modelId: "llama3");

        int? result = await provider.ProbeLoadedContextLengthAsync();

        Assert.Equal(8192, result);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("http://localhost:1234/api/show", handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("\"name\":\"llama3\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task Probe_Ollama_NoNumCtxLine_ReturnsNull()
    {
        const string body = """
            { "parameters": "stop <|eot|>\ntemperature 0.7" }
            """;
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.OK, body));
        var provider = BuildProvider(ContextLengthProbe.Ollama, handler);

        int? result = await provider.ProbeLoadedContextLengthAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task Probe_LlamaCpp_TopLevelNCtx_Returns()
    {
        const string body = """{ "n_ctx": 4096, "total_slots": 1 }""";
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.OK, body));
        var provider = BuildProvider(ContextLengthProbe.LlamaCpp, handler);

        int? result = await provider.ProbeLoadedContextLengthAsync();

        Assert.Equal(4096, result);
        Assert.Equal("http://localhost:1234/props", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Probe_LlamaCpp_NestedNCtx_Returns()
    {
        const string body = """
            { "default_generation_settings": { "n_ctx": 2048 }, "total_slots": 1 }
            """;
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.OK, body));
        var provider = BuildProvider(ContextLengthProbe.LlamaCpp, handler);

        int? result = await provider.ProbeLoadedContextLengthAsync();

        Assert.Equal(2048, result);
    }

    [Fact]
    public async Task Probe_HttpException_ReturnsNull()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));
        var provider = BuildProvider(ContextLengthProbe.LMStudio, handler);

        int? result = await provider.ProbeLoadedContextLengthAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task Preflight_ProbedLessThanConfigured_ReturnsContextLengthFailure()
    {
        const string body = """
            { "data": [ { "id": "test-model", "loaded_context_length": 2048 } ] }
            """;
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.OK, body));
        var provider = BuildProvider(ContextLengthProbe.LMStudio, handler, modelId: "test-model",
            chatFacade: new SuccessChatFacade("test-model"));

        var failure = await provider.PreflightAsync(
            configuredMaxContextTokens: 4096,
            logger: NullLogger<OpenAICompatibleLlmProvider>.Instance);

        Assert.NotNull(failure);
        Assert.Equal("Llm.ContextLength", failure!.Stage);
        Assert.Contains("2048", failure.Reason);
        Assert.Contains("4096", failure.Reason);
    }

    [Fact]
    public async Task Preflight_ProbedEqualConfigured_ReturnsNull()
    {
        const string body = """
            { "data": [ { "id": "test-model", "loaded_context_length": 4096 } ] }
            """;
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.OK, body));
        var provider = BuildProvider(ContextLengthProbe.LMStudio, handler, modelId: "test-model",
            chatFacade: new SuccessChatFacade("test-model"));

        var failure = await provider.PreflightAsync(4096, NullLogger<OpenAICompatibleLlmProvider>.Instance);

        Assert.Null(failure);
    }

    [Fact]
    public async Task Preflight_ProbedGreaterThanConfigured_ReturnsNull()
    {
        const string body = """
            { "data": [ { "id": "test-model", "loaded_context_length": 8192 } ] }
            """;
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.OK, body));
        var provider = BuildProvider(ContextLengthProbe.LMStudio, handler, modelId: "test-model",
            chatFacade: new SuccessChatFacade("test-model"));

        var failure = await provider.PreflightAsync(4096, NullLogger<OpenAICompatibleLlmProvider>.Instance);

        Assert.Null(failure);
    }

    [Fact]
    public async Task Preflight_ProbeUnknown_ReturnsNullSilent()
    {
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.NotFound, ""));
        var provider = BuildProvider(ContextLengthProbe.LMStudio, handler, modelId: "test-model",
            chatFacade: new SuccessChatFacade("test-model"));

        var failure = await provider.PreflightAsync(4096, NullLogger<OpenAICompatibleLlmProvider>.Instance);

        Assert.Null(failure);
    }

    [Fact]
    public async Task Preflight_ProbeNone_DoesNotMakeHttpCall()
    {
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.NotFound, ""));
        var provider = BuildProvider(ContextLengthProbe.None, handler, modelId: "test-model",
            chatFacade: new SuccessChatFacade("test-model"));

        var failure = await provider.PreflightAsync(4096, NullLogger<OpenAICompatibleLlmProvider>.Instance);

        Assert.Null(failure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Preflight_ChatAvailabilityFails_ShortCircuitsBeforeProbe()
    {
        var handler = new RecordingHandler(StubResponse(HttpStatusCode.OK, "{}"));
        var provider = BuildProvider(ContextLengthProbe.LMStudio, handler, modelId: "test-model",
            chatFacade: new FailingChatFacade("server unreachable"));

        var failure = await provider.PreflightAsync(4096, NullLogger<OpenAICompatibleLlmProvider>.Instance);

        Assert.NotNull(failure);
        Assert.Equal("Llm", failure!.Stage);
        Assert.Empty(handler.Requests);
    }

    private sealed class SuccessChatFacade : IChatClientFacade
    {
        private readonly string _modelId;
        public SuccessChatFacade(string modelId) => _modelId = modelId;

        public Task<OpenAI.Chat.ChatCompletion> CompleteChatAsync(
            IList<OpenAI.Chat.ChatMessage> messages,
            OpenAI.Chat.ChatCompletionOptions options,
            CancellationToken ct)
        {
#pragma warning disable OPENAI001
            var completion = OpenAI.Chat.OpenAIChatModelFactory.ChatCompletion(
                model: _modelId,
                content: new OpenAI.Chat.ChatMessageContent("ok"));
#pragma warning restore OPENAI001
            return Task.FromResult(completion);
        }
    }

    private sealed class FailingChatFacade : IChatClientFacade
    {
        private readonly string _message;
        public FailingChatFacade(string message) => _message = message;

        public Task<OpenAI.Chat.ChatCompletion> CompleteChatAsync(
            IList<OpenAI.Chat.ChatMessage> messages,
            OpenAI.Chat.ChatCompletionOptions options,
            CancellationToken ct)
            => throw new HttpRequestException(_message);
    }

    private static OpenAICompatibleLlmProvider BuildProvider(
        ContextLengthProbe probe,
        HttpMessageHandler handler,
        string modelId = "test-model",
        IChatClientFacade? chatFacade = null)
    {
        var openAIClient = new OpenAI.OpenAIClient(
            new System.ClientModel.ApiKeyCredential("no-key"),
            new OpenAI.OpenAIClientOptions { Endpoint = BaseEndpoint });
        var chatClient = openAIClient.GetChatClient(modelId);
        var rateLimiter = new RateLimiter(concurrency: 1, requestsPerMinute: null);
        return new OpenAICompatibleLlmProvider(
            chatClient,
            rateLimiter,
            modelId,
            BaseEndpoint,
            probe,
            probeFacade: chatFacade,
            probeHandler: handler);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> StubResponse(HttpStatusCode status, string body)
        => _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<RecordedRequest> Requests { get; } = new();

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri, body));
            return _responder(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri? RequestUri, string Body);
}
