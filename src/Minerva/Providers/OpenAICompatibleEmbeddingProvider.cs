using System.ClientModel;
using Microsoft.Extensions.AI;
using Minerva.Exceptions;
using Minerva.Ingestion;
using Polly;
using Polly.Retry;

namespace Minerva.Providers;

public sealed class OpenAICompatibleEmbeddingProvider
    : IEmbeddingGenerator<string, Embedding<float>>, IEmbeddingClient
{
    private readonly OpenAI.Embeddings.EmbeddingClient _client;
    private readonly RateLimiter _rateLimiter;
    private readonly ResiliencePipeline _resiliencePipeline;

    public OpenAICompatibleEmbeddingProvider(
        OpenAI.Embeddings.EmbeddingClient client,
        RateLimiter rateLimiter,
        string modelId,
        Uri endpoint)
    {
        _client = client;
        _rateLimiter = rateLimiter;
        Metadata = new EmbeddingGeneratorMetadata(
            nameof(OpenAICompatibleEmbeddingProvider), endpoint, modelId);
        _resiliencePipeline = BuildResiliencePipeline();
    }

    public EmbeddingGeneratorMetadata Metadata { get; }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var valuesList = values.ToList();
        if (valuesList.Count == 0)
            return new GeneratedEmbeddings<Embedding<float>>();

        try
        {
            OpenAI.Embeddings.OpenAIEmbeddingCollection result =
                await _resiliencePipeline.ExecuteAsync(async ct =>
                {
                    await _rateLimiter.AcquireAsync(ct);
                    try
                    {
                        OpenAI.Embeddings.OpenAIEmbeddingCollection embeddings =
                            await _client.GenerateEmbeddingsAsync(valuesList, cancellationToken: ct);
                        return embeddings;
                    }
                    finally
                    {
                        _rateLimiter.Release();
                    }
                }, cancellationToken);

            var generated = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var item in result)
            {
                var vector = item.ToFloats().ToArray();
                L2Normalize(vector);
                generated.Add(new Embedding<float>(vector));
            }

            return generated;
        }
        catch (ClientResultException ex)
        {
            throw new EmbeddingException(
                $"Embedding API request failed (HTTP {ex.Status}): {ex.Message}", ex);
        }
        catch (EmbeddingException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EmbeddingException($"Failed to generate embeddings: {ex.Message}", ex);
        }
    }

    internal static void L2Normalize(Span<float> vector)
    {
        float sumSquares = 0;
        for (int i = 0; i < vector.Length; i++)
            sumSquares += vector[i] * vector[i];

        if (sumSquares == 0f)
            return;

        float norm = MathF.Sqrt(sumSquares);
        for (int i = 0; i < vector.Length; i++)
            vector[i] /= norm;
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var generated = await GenerateAsync(texts, options: null, ct);
        var result = new float[generated.Count][];
        for (int i = 0; i < generated.Count; i++)
            result[i] = generated[i].Vector.ToArray();
        return result;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null) return null;
        if (serviceType == typeof(EmbeddingGeneratorMetadata)) return Metadata;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose() => _rateLimiter.Dispose();

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
