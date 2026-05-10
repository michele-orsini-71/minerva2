using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Minerva.Exceptions;
using Minerva.Ingestion;
using Minerva.Models;
using Polly;
using Polly.Retry;
using OAI = OpenAI.Chat;

namespace Minerva.Providers;

internal interface IChatClientFacade
{
    Task<OAI.ChatCompletion> CompleteChatAsync(
        IList<OAI.ChatMessage> messages,
        OAI.ChatCompletionOptions options,
        CancellationToken ct);
}

public sealed class OpenAICompatibleLlmProvider : IChatClient, ILlmClient, ILlmAvailabilityProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly OAI.ChatClient _client;
    private readonly RateLimiter _rateLimiter;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly IChatClientFacade _probeFacade;
    private readonly ContextLengthProbe _contextLengthProbe;
    private readonly HttpMessageHandler? _probeHandler;

    public OpenAICompatibleLlmProvider(
        OAI.ChatClient client,
        RateLimiter rateLimiter,
        string modelId,
        Uri endpoint,
        ContextLengthProbe contextLengthProbe)
        : this(client, rateLimiter, modelId, endpoint, contextLengthProbe, probeFacade: null, probeHandler: null)
    {
    }

    internal OpenAICompatibleLlmProvider(
        OAI.ChatClient client,
        RateLimiter rateLimiter,
        string modelId,
        Uri endpoint,
        ContextLengthProbe contextLengthProbe,
        IChatClientFacade? probeFacade,
        HttpMessageHandler? probeHandler)
    {
        _client = client;
        _rateLimiter = rateLimiter;
        Metadata = new ChatClientMetadata(
            nameof(OpenAICompatibleLlmProvider), endpoint, modelId);
        _resiliencePipeline = BuildResiliencePipeline();
        _probeFacade = probeFacade ?? new SdkChatClientFacade(_client);
        _contextLengthProbe = contextLengthProbe;
        _probeHandler = probeHandler;
    }

    public ChatClientMetadata Metadata { get; }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var openAiMessages = chatMessages.Select(ToOpenAIMessage).ToList();
        var openAiOptions = MapOptions(options);

        try
        {
            OAI.ChatCompletion completion = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                await _rateLimiter.AcquireAsync(ct);
                try
                {
                    OAI.ChatCompletion result =
                        await _client.CompleteChatAsync(openAiMessages, openAiOptions, ct);
                    return result;
                }
                finally
                {
                    _rateLimiter.Release();
                }
            }, cancellationToken);

            var responseText = completion.Content.Count > 0
                ? completion.Content[0].Text ?? string.Empty
                : string.Empty;

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText));
        }
        catch (ClientResultException ex)
        {
            throw new ProviderUnavailableException(
                $"LLM API request failed (HTTP {ex.Status}): {ReadResponseBody(ex) ?? ex.Message}", ex);
        }
        catch (ProviderUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ProviderUnavailableException(
                $"Failed to get chat response: {ex.Message}", ex);
        }
    }

    private static string? ReadResponseBody(ClientResultException ex)
    {
        try
        {
            var body = ex.GetRawResponse()?.Content?.ToString();
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch
        {
            return null;
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var openAiMessages = chatMessages.Select(ToOpenAIMessage).ToList();
        var openAiOptions = MapOptions(options);
        var acquired = false;
        IAsyncEnumerable<OAI.StreamingChatCompletionUpdate> stream;

        try
        {
            await _rateLimiter.AcquireAsync(cancellationToken);
            acquired = true;
            stream = await _resiliencePipeline.ExecuteAsync(
                ct => ValueTask.FromResult(
                    _client.CompleteChatStreamingAsync(openAiMessages, openAiOptions, ct)),
                cancellationToken);
        }
        catch (ClientResultException ex)
        {
            if (acquired)
                _rateLimiter.Release();
            throw new ProviderUnavailableException(
                $"LLM streaming API request failed (HTTP {ex.Status}): {ReadResponseBody(ex) ?? ex.Message}", ex);
        }
        catch (ProviderUnavailableException)
        {
            if (acquired)
                _rateLimiter.Release();
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (acquired)
                _rateLimiter.Release();
            throw new ProviderUnavailableException(
                $"Failed to get streaming chat response: {ex.Message}", ex);
        }

        try
        {
            await foreach (var update in stream.WithCancellation(cancellationToken))
            {
                foreach (var part in update.ContentUpdate)
                {
                    if (part.Text is not null)
                    {
                        var chatUpdate = new ChatResponseUpdate();
                        chatUpdate.Role = ChatRole.Assistant;
                        chatUpdate.Contents.Add(new TextContent(part.Text));
                        yield return chatUpdate;
                    }
                }
            }
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    public async Task CheckAvailabilityAsync(CancellationToken ct = default)
    {
        var options = new OAI.ChatCompletionOptions { MaxOutputTokenCount = 5 };
        var messages = new List<OAI.ChatMessage> { new OAI.UserChatMessage("ping") };
        var completion = await _probeFacade.CompleteChatAsync(messages, options, ct);

        var requested = Metadata.DefaultModelId;
        var served = completion.Model;
        if (!string.Equals(served, requested, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Server responded with model '{served}', not the configured '{requested}'. " +
                $"Check the model name in configuration matches exactly what the server exposes.");
        }
    }

    public async Task<PreflightFailure?> PreflightAsync(
        int configuredMaxContextTokens,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            await CheckAvailabilityAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PreflightFailure(
                "Llm",
                $"LLM endpoint at '{Metadata.ProviderUri}' did not respond, or model '{Metadata.DefaultModelId}' is not available: {ex.Message}. Verify the endpoint URL, the API key, and the model name.",
                ex);
        }

        if (_contextLengthProbe == ContextLengthProbe.None)
            return null;

        int? probed = await ProbeLoadedContextLengthAsync(ct);

        if (probed is null)
        {
            logger.LogWarning(
                "LLM context-length probe ({Probe}) at {Uri} did not respond as expected; using configured MaxContextTokens={Configured} unverified.",
                _contextLengthProbe, Metadata.ProviderUri, configuredMaxContextTokens);
            return null;
        }

        if (probed.Value < configuredMaxContextTokens)
        {
            return new PreflightFailure(
                "Llm.ContextLength",
                $"{_contextLengthProbe} reports loaded context length = {probed.Value} tokens, but Chunking.ContextBudget.MaxContextTokens is configured to {configuredMaxContextTokens}. Either lower MaxContextTokens to <= {probed.Value} or load the model with a larger context window.",
                null);
        }

        logger.LogInformation(
            "LLM context-length probe ({Probe}) reports loaded={Loaded} tokens; configured MaxContextTokens={Configured}. OK.",
            _contextLengthProbe, probed.Value, configuredMaxContextTokens);
        return null;
    }

    public ContextLengthProbe ContextLengthProbe => _contextLengthProbe;

    internal async Task<int?> ProbeLoadedContextLengthAsync(CancellationToken ct = default)
    {
        if (_contextLengthProbe == ContextLengthProbe.None)
            return null;

        var serverRoot = Metadata.ProviderUri!.GetLeftPart(UriPartial.Authority);
        var modelId = Metadata.DefaultModelId!;

        using var http = CreateProbeHttpClient();
        try
        {
            return _contextLengthProbe switch
            {
                ContextLengthProbe.LMStudio => await ProbeLMStudioAsync(http, serverRoot, modelId, ct),
                ContextLengthProbe.Ollama => await ProbeOllamaAsync(http, serverRoot, modelId, ct),
                ContextLengthProbe.LlamaCpp => await ProbeLlamaCppAsync(http, serverRoot, ct),
                _ => null,
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private HttpClient CreateProbeHttpClient()
    {
        HttpClient http = _probeHandler is null
            ? new HttpClient()
            : new HttpClient(_probeHandler, disposeHandler: false);
        http.Timeout = ProbeTimeout;
        return http;
    }

    private static async Task<int?> ProbeLMStudioAsync(
        HttpClient http, string serverRoot, string modelId, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"{serverRoot}/api/v0/models", ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var entry in data.EnumerateArray())
        {
            if (!entry.TryGetProperty("id", out var idElem)) continue;
            if (!string.Equals(idElem.GetString(), modelId, StringComparison.Ordinal)) continue;
            if (entry.TryGetProperty("loaded_context_length", out var lc) && lc.TryGetInt32(out int n))
                return n;
            return null;
        }
        return null;
    }

    private static async Task<int?> ProbeOllamaAsync(
        HttpClient http, string serverRoot, string modelId, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { name = modelId });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync($"{serverRoot}/api/show", content, ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("parameters", out var parameters)) return null;
        var paramsText = parameters.GetString();
        if (string.IsNullOrEmpty(paramsText)) return null;

        foreach (var line in paramsText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("num_ctx", StringComparison.Ordinal)) continue;
            var rest = trimmed.AsSpan("num_ctx".Length).TrimStart();
            if (int.TryParse(rest, out int n)) return n;
        }
        return null;
    }

    private static async Task<int?> ProbeLlamaCppAsync(
        HttpClient http, string serverRoot, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"{serverRoot}/props", ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (doc.RootElement.TryGetProperty("default_generation_settings", out var settings)
            && settings.ValueKind == JsonValueKind.Object
            && settings.TryGetProperty("n_ctx", out var nested)
            && nested.TryGetInt32(out int nestedN))
            return nestedN;

        if (doc.RootElement.TryGetProperty("n_ctx", out var top) && top.TryGetInt32(out int topN))
            return topN;

        return null;
    }

    public async Task<string> GenerateAsync(
        string? systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>();
        if (systemPrompt is not null)
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));
        messages.Add(new ChatMessage(ChatRole.User, userPrompt));

        var response = await GetResponseAsync(messages, options: null, ct);
        return response.Text ?? string.Empty;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null) return null;
        if (serviceType == typeof(ChatClientMetadata)) return Metadata;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose() => _rateLimiter.Dispose();

    private static OAI.ChatMessage ToOpenAIMessage(ChatMessage message)
    {
        var text = message.Text ?? string.Empty;
        if (message.Role == ChatRole.System)
            return new OAI.SystemChatMessage(text);
        if (message.Role == ChatRole.Assistant)
            return new OAI.AssistantChatMessage(text);
        return new OAI.UserChatMessage(text);
    }

    private static OAI.ChatCompletionOptions? MapOptions(ChatOptions? options)
    {
        if (options is null) return null;

        var result = new OAI.ChatCompletionOptions();
        if (options.Temperature.HasValue)
            result.Temperature = options.Temperature.Value;
        if (options.MaxOutputTokens.HasValue)
            result.MaxOutputTokenCount = options.MaxOutputTokens.Value;
        if (options.TopP.HasValue)
            result.TopP = options.TopP.Value;
        return result;
    }

    private sealed class SdkChatClientFacade : IChatClientFacade
    {
        private readonly OAI.ChatClient _client;

        public SdkChatClientFacade(OAI.ChatClient client) => _client = client;

        public async Task<OAI.ChatCompletion> CompleteChatAsync(
            IList<OAI.ChatMessage> messages,
            OAI.ChatCompletionOptions options,
            CancellationToken ct)
        {
            var result = await _client.CompleteChatAsync(messages, options, ct);
            return result.Value;
        }
    }

    private static ResiliencePipeline BuildResiliencePipeline() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<ClientResultException>(ex => ex.Status is 429 or >= 500)
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(1),
                UseJitter = true,
            })
            .Build();
}
