using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Minerva.Exceptions;
using Minerva.Ingestion;
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
    private readonly OAI.ChatClient _client;
    private readonly RateLimiter _rateLimiter;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly IChatClientFacade _chatFacade;

    public OpenAICompatibleLlmProvider(
        OAI.ChatClient client,
        RateLimiter rateLimiter,
        string modelId,
        Uri endpoint)
        : this(client, rateLimiter, modelId, endpoint, chatFacade: null)
    {
    }

    internal OpenAICompatibleLlmProvider(
        OAI.ChatClient client,
        RateLimiter rateLimiter,
        string modelId,
        Uri endpoint,
        IChatClientFacade? chatFacade)
    {
        _client = client;
        _rateLimiter = rateLimiter;
        Metadata = new ChatClientMetadata(
            nameof(OpenAICompatibleLlmProvider), endpoint, modelId);
        _resiliencePipeline = BuildResiliencePipeline();
        _chatFacade = chatFacade ?? new SdkChatClientFacade(_client);
    }

    public ChatClientMetadata Metadata { get; }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messagesList = chatMessages.ToList();
        var openAiOptions = MapOptions(options);

        try
        {
            var openAiMessages = messagesList.Select(ToOpenAIMessage).ToList();
            var completion = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                await _rateLimiter.AcquireAsync(ct);
                try
                {
                    return await _chatFacade.CompleteChatAsync(openAiMessages, openAiOptions!, ct);
                }
                finally
                {
                    _rateLimiter.Release();
                }
            }, cancellationToken);
            return ToChatResponse(completion);
        }
        catch (ClientResultException ex) when (ex.Status == 400)
        {
            var body = ReadResponseBody(ex);
            int inputChars = LastUserMessageLength(messagesList);
            throw new LlmContextOverflowException(
                $"LLM rejected request (HTTP 400): {body ?? ex.Message}", ex)
            {
                InputChars = inputChars,
                ServerResponseBody = body,
            };
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

    private static int LastUserMessageLength(IReadOnlyList<ChatMessage> messages)
    {
        for (int i = messages.Count - 1; i >= 0; i--)
            if (messages[i].Role == ChatRole.User) return messages[i].Text?.Length ?? 0;
        return 0;
    }

    private static ChatResponse ToChatResponse(OAI.ChatCompletion completion)
    {
        var responseText = completion.Content.Count > 0
            ? completion.Content[0].Text ?? string.Empty
            : string.Empty;
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText));
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
        var completion = await _chatFacade.CompleteChatAsync(messages, options, ct);

        var requested = Metadata.DefaultModelId;
        var served = completion.Model;
        if (!string.Equals(served, requested, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Server responded with model '{served}', not the configured '{requested}'. " +
                $"Check the model name in configuration matches exactly what the server exposes.");
        }
    }

    public async Task<PreflightFailure?> PreflightAsync(CancellationToken ct = default)
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
