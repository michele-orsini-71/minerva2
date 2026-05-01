using System.ClientModel;
using Minerva.Ingestion;
using Minerva.Models;

namespace Minerva.Providers;

public class ProviderFactory
{
    private readonly ProviderOptions _embedding;
    private readonly ProviderOptions? _llm;
    private readonly CredentialResolver? _embeddingResolver;
    private readonly CredentialResolver? _llmResolver;

    public ProviderFactory(ProviderOptions embedding, ProviderOptions? llm)
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
        var (client, endpoint) = CreateOpenAIClient(_embedding, _embeddingResolver);
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

        var (client, endpoint) = CreateOpenAIClient(_llm, _llmResolver);
        var chatClient = client.GetChatClient(_llm.Model);
        var rateLimiter = new RateLimiter(_llm.Concurrency, _llm.RequestsPerMinute);
        return new OpenAICompatibleLlmProvider(chatClient, rateLimiter, _llm.Model, endpoint);
    }

    private static (OpenAI.OpenAIClient Client, Uri Endpoint) CreateOpenAIClient(
        ProviderOptions options,
        CredentialResolver? resolver)
    {
        var apiKey = resolver?.Resolve() ?? "no-key-required";
        var endpoint = new Uri(options.BaseUrl);
        var clientOptions = new OpenAI.OpenAIClientOptions { Endpoint = endpoint };
        var client = new OpenAI.OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        return (client, endpoint);
    }
}
