using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Minerva.Exceptions;
using Minerva.Search;
using Polly;
using Polly.Retry;

namespace Minerva.Providers;

record RerankResponseResult(int index, float relevance_score);

record RerankResponse(string model, string _object, object usage, List<RerankResponseResult> results);
record RerankRequest(string model, string query, List<string> documents);

class HttpRerankerProvider : IRerankerClient
{
    readonly HttpClient _http;
    readonly string _modelId;
    readonly ResiliencePipeline _relisiencePipeline;

    // public HttpRerankerProvider(Uri endpoint)
    public HttpRerankerProvider(string modelId, Uri endpointUri)
        : this(modelId, endpointUri, new HttpClientHandler(), retryBaseDelay: null)
    {
    }

    internal HttpRerankerProvider(
        string modelId, Uri endpointUri, HttpMessageHandler handler, TimeSpan? retryBaseDelay)
    {
        _http = new HttpClient(handler) { BaseAddress = endpointUri };
        _relisiencePipeline = BuildResiliencePipeline(retryBaseDelay ?? TimeSpan.FromSeconds(1));
        _modelId = modelId;
    }

    private async Task<RerankResponse?> GetResponse(RerankRequest request, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync("/rerank", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RerankResponse>(cancellationToken);
    }

    public async Task<float[]> RankTexts(string query, List<string> texts, CancellationToken cancellationToken)
    {
        return await _relisiencePipeline.ExecuteAsync(async ct => { 

            var request = new RerankRequest(_modelId, query, texts);
            var result = await GetResponse(request, ct)
                 ?? throw new InvalidOperationException("Reranker returned an empty response body.");

            var scores = new float[texts.Count];
            foreach (var r in result.results)
                scores[r.index] = r.relevance_score;
            return scores;
         }, cancellationToken);
    }

    private static ResiliencePipeline BuildResiliencePipeline(TimeSpan baseDelay) =>
    new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>(ex =>
                    ex.StatusCode is null or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError)
                .Handle<TaskCanceledException>(ex => ex.InnerException is TimeoutException),
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = baseDelay,
            UseJitter = true,
        })
        .Build();
    
    public async Task CheckAvailabilityAsync(CancellationToken ct = default)
    {
        var request = new RerankRequest(_modelId,  "What is the largest animal phylum", 
            ["Arthropods are the largest phylum of animals", "Mollusca is a large phylum", "The sky is blue"]);
        var response = await GetResponse(request, ct) ??
         throw new InvalidOperationException("Reranker returned an empty response body.");
                
        if (!string.Equals(_modelId, response.model, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Server responded with model '{response.model}', not the configured '{_modelId}'. " +
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
                "Reranker",
                $"Reranker endpoint at '{_http.BaseAddress}' did not respond, or model '{_modelId}' is not available: {ex.Message}. Verify the endpoint URL, the API key, and the model name.",
                ex);
        }

        return null;
    }
}