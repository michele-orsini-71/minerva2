using System.ClientModel;
using Minerva.Ingestion;
using Minerva.Models;

namespace Minerva.Providers;

public class ProviderFactory
{
    private readonly EmbeddingProviderOptions _embedding;
    private readonly LlmProviderOptions? _llm;
    private readonly CredentialResolver? _embeddingResolver;
    private readonly CredentialResolver? _llmResolver;

    public ProviderFactory(EmbeddingProviderOptions embedding, LlmProviderOptions? llm)
    {
        _embedding = embedding;
        _embeddingResolver = string.IsNullOrWhiteSpace(embedding.ApiKey)
            ? null
            : new CredentialResolver(embedding.ApiKey);

        _llm = llm;
        _llmResolver = string.IsNullOrWhiteSpace(llm?.ApiKey)
            ? null
            : new CredentialResolver(llm.ApiKey);
    }

    public IEmbeddingClient CreateEmbeddingProvider()
    {
        var (client, endpoint) = CreateOpenAIClient(_embedding.BaseUrl, _embeddingResolver);
        var embeddingClient = client.GetEmbeddingClient(_embedding.Model);
        var rateLimiter = new RateLimiter(_embedding.Concurrency, _embedding.RequestsPerMinute);
        return new OpenAICompatibleEmbeddingProvider(embeddingClient, rateLimiter, _embedding.Model, endpoint);
    }

    public bool HasLlm => _llm is not null;

    public ILlmClient CreateLlmProvider()
    {
        if (_llm is null)
            throw new InvalidOperationException(
                "ProviderFactory was constructed without LLM options. Check HasLlm before calling.");

        var (client, endpoint) = CreateOpenAIClient(_llm.BaseUrl, _llmResolver);
        var chatClient = client.GetChatClient(_llm.Model);
        var rateLimiter = new RateLimiter(_llm.Concurrency, _llm.RequestsPerMinute);
        return new OpenAICompatibleLlmProvider(chatClient, rateLimiter, _llm.Model, endpoint, _llm.ContextLengthProbe);
    }

    private static (OpenAI.OpenAIClient Client, Uri Endpoint) CreateOpenAIClient(
        string baseUrl,
        CredentialResolver? resolver)
    {
        var apiKey = resolver?.Resolve() ?? "no-key-required";
        var endpoint = new Uri(baseUrl);
        var clientOptions = new OpenAI.OpenAIClientOptions { Endpoint = endpoint };
        var client = new OpenAI.OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        return (client, endpoint);
    }
}
