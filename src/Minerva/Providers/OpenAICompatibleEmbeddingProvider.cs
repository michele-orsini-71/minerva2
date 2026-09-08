using System.ClientModel;
using Microsoft.Extensions.AI;
using Minerva.Exceptions;
using Minerva.Ingestion;
using Polly;
using Polly.Retry;

namespace Minerva.Providers;

internal interface IEmbeddingProbeFacade
{
    Task<int> EmbedAndCountDimensionsAsync(string input, CancellationToken ct);
}

internal interface IEmbeddingGenerationFacade
{
    Task<OpenAI.Embeddings.OpenAIEmbeddingCollection> GenerateEmbeddingsAsync(
        IList<string> inputs, CancellationToken ct);
}

public sealed class OpenAICompatibleEmbeddingProvider
    : IEmbeddingGenerator<string, Embedding<float>>, IEmbeddingClient, IEmbeddingDimensionProvider
{
    private readonly OpenAI.Embeddings.EmbeddingClient _client;
    private readonly RateLimiter _rateLimiter;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly IEmbeddingProbeFacade _probeFacade;
    private readonly IEmbeddingGenerationFacade _generationFacade;
    private readonly Lazy<Task<int>> _dimensionLazy;

    public OpenAICompatibleEmbeddingProvider(
        OpenAI.Embeddings.EmbeddingClient client,
        RateLimiter rateLimiter,
        string modelId,
        Uri endpoint)
        : this(client, rateLimiter, modelId, endpoint, probeFacade: null)
    {
    }

    internal OpenAICompatibleEmbeddingProvider(
        OpenAI.Embeddings.EmbeddingClient client,
        RateLimiter rateLimiter,
        string modelId,
        Uri endpoint,
        IEmbeddingProbeFacade? probeFacade,
        IEmbeddingGenerationFacade? generationFacade = null,
        TimeSpan? retryBaseDelay = null)
    {
        _client = client;
        _rateLimiter = rateLimiter;
        Metadata = new EmbeddingGeneratorMetadata(
            nameof(OpenAICompatibleEmbeddingProvider), endpoint, modelId);
        _resiliencePipeline = BuildResiliencePipeline(retryBaseDelay ?? TimeSpan.FromSeconds(1));
        _probeFacade = probeFacade ?? new SdkEmbeddingProbeFacade(_client);
        _generationFacade = generationFacade ?? new SdkEmbeddingGenerationFacade(_client);
        _dimensionLazy = new Lazy<Task<int>>(
            () => ProbeDimensionCoreAsync(CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication);
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
                        return await _generationFacade.GenerateEmbeddingsAsync(valuesList, ct);
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

    public Task<int> GetDimensionAsync(CancellationToken ct = default) =>
        _dimensionLazy.Value.WaitAsync(ct);

    public async Task<PreflightFailure?> PreflightAsync(CancellationToken ct = default)
    {
        try
        {
            await GetDimensionAsync(ct);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PreflightFailure(
                "Embedding",
                $"Embedding endpoint at '{Metadata.ProviderUri}' did not respond, or model '{Metadata.DefaultModelId}' is not available: {ex.Message}. Verify the endpoint URL, the API key, and the model name. For local stacks (Ollama / LM Studio) confirm the model is loaded.",
                ex);
        }
    }

    private async Task<int> ProbeDimensionCoreAsync(CancellationToken ct)
    {
        try
        {
            return await _probeFacade.EmbedAndCountDimensionsAsync("preflight", ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ClientResultException ex)
        {
            throw new ProviderUnavailableException(
                $"Embedding dimension probe failed (HTTP {ex.Status}): {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new ProviderUnavailableException(
                $"Embedding dimension probe failed: {ex.Message}", ex);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null) return null;
        if (serviceType == typeof(EmbeddingGeneratorMetadata)) return Metadata;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose() => _rateLimiter.Dispose();

    private static ResiliencePipeline BuildResiliencePipeline(TimeSpan baseDelay) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<ClientResultException>(ex => ex.Status is 429 or >= 500)
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = baseDelay,
                UseJitter = true,
            })
            .Build();

    private sealed class SdkEmbeddingProbeFacade : IEmbeddingProbeFacade
    {
        private readonly OpenAI.Embeddings.EmbeddingClient _client;

        public SdkEmbeddingProbeFacade(OpenAI.Embeddings.EmbeddingClient client) => _client = client;

        public async Task<int> EmbedAndCountDimensionsAsync(string input, CancellationToken ct)
        {
            OpenAI.Embeddings.OpenAIEmbeddingCollection embeddings =
                await _client.GenerateEmbeddingsAsync(new[] { input }, cancellationToken: ct);
            if (embeddings.Count == 0)
                throw new ProviderUnavailableException(
                    "Embedding dimension probe returned no embeddings.");
            return embeddings[0].ToFloats().Length;
        }
    }

    private sealed class SdkEmbeddingGenerationFacade : IEmbeddingGenerationFacade
    {
        private readonly OpenAI.Embeddings.EmbeddingClient _client;

        public SdkEmbeddingGenerationFacade(OpenAI.Embeddings.EmbeddingClient client) => _client = client;

        public async Task<OpenAI.Embeddings.OpenAIEmbeddingCollection> GenerateEmbeddingsAsync(
            IList<string> inputs, CancellationToken ct) =>
            await _client.GenerateEmbeddingsAsync(inputs, cancellationToken: ct);
    }
}
