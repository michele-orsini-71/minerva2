using System.ClientModel;
using Microsoft.Extensions.AI;
using Minerva.Configuration;

namespace Minerva.Providers;

public class ProviderFactory
{
    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingProvider(ProviderOptions options)
    {
        var (client, endpoint) = CreateOpenAIClient(options);
        var embeddingClient = client.GetEmbeddingClient(options.Model);
        var rateLimiter = new RateLimiter(options.Concurrency, options.RequestsPerMinute);
        return new OpenAICompatibleEmbeddingProvider(embeddingClient, rateLimiter, options.Model, endpoint);
    }

    public IChatClient? CreateLlmProvider(ProviderOptions? options)
    {
        if (options is null) return null;

        var (client, endpoint) = CreateOpenAIClient(options);
        var chatClient = client.GetChatClient(options.Model);
        var rateLimiter = new RateLimiter(options.Concurrency, options.RequestsPerMinute);
        return new OpenAICompatibleLlmProvider(chatClient, rateLimiter, options.Model, endpoint);
    }

    private static (OpenAI.OpenAIClient Client, Uri Endpoint) CreateOpenAIClient(ProviderOptions options)
    {
        var apiKey = options.ApiKey is not null
            ? CredentialResolver.Resolve(options.ApiKey)
            : "no-key-required";
        var endpoint = new Uri(options.BaseUrl);
        var clientOptions = new OpenAI.OpenAIClientOptions { Endpoint = endpoint };
        var client = new OpenAI.OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        return (client, endpoint);
    }
}
