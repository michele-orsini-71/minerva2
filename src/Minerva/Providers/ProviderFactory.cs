using System.ClientModel;
using Minerva.Ingestion;
using Minerva.Models;

namespace Minerva.Providers;

internal class ProviderFactory
{
    private readonly EmbeddingProviderOptions _embedding;
    private readonly CredentialResolver? _embeddingResolver;

    public ProviderFactory(EmbeddingProviderOptions embedding)
    {
        _embedding = embedding;
        _embeddingResolver = string.IsNullOrWhiteSpace(embedding.ApiKey)
            ? null
            : new CredentialResolver(embedding.ApiKey);
    }

    public IEmbeddingClient CreateEmbeddingProvider()
    {
        var (client, endpoint) = CreateOpenAIClient(_embedding.BaseUrl, _embeddingResolver);
        var embeddingClient = client.GetEmbeddingClient(_embedding.Model);
        var rateLimiter = new RateLimiter(_embedding.Concurrency, _embedding.RequestsPerMinute);
        return new OpenAICompatibleEmbeddingProvider(embeddingClient, rateLimiter, _embedding.Model, endpoint);
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
