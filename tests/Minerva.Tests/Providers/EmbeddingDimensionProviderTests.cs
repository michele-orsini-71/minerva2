using System.ClientModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minerva.Configuration;
using Minerva.DI;
using Minerva.Exceptions;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Providers;

namespace Minerva.Tests.Providers;

[Trait("Category", "Providers")]
public class EmbeddingDimensionProviderTests
{
    [Fact]
    public async Task GetDimensionAsync_FiftyConcurrentCallers_InvokesProbeOnce()
    {
        var facade = new FakeProbeFacade { Dimension = 1536 };
        var provider = CreateProvider(facade);

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => provider.GetDimensionAsync(CancellationToken.None))
            .ToArray();

        int[] results = await Task.WhenAll(tasks);

        Assert.All(results, d => Assert.Equal(1536, d));
        Assert.Equal(1, facade.CallCount);
    }

    [Fact]
    public async Task GetDimensionAsync_FirstCallerCancels_OtherCallerStillSeesDimension_AndProbeRunsOnce()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var facade = new FakeProbeFacade { Custom = (_, _) => gate.Task };
        var provider = CreateProvider(facade);

        using var earlyCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        Task<int> doomed = provider.GetDimensionAsync(earlyCts.Token);
        Task<int> healthy = provider.GetDimensionAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doomed);

        gate.SetResult(768);

        int dimension = await healthy;
        Assert.Equal(768, dimension);
        Assert.Equal(1, facade.CallCount);
    }

    [Fact]
    public async Task GetDimensionAsync_FacadeThrows_FailureIsCachedAcrossCalls()
    {
        var facade = new FakeProbeFacade
        {
            Custom = (_, _) => throw new HttpRequestException("boom"),
        };
        var provider = CreateProvider(facade);

        var first = await Assert.ThrowsAsync<ProviderUnavailableException>(
            () => provider.GetDimensionAsync(CancellationToken.None));
        var second = await Assert.ThrowsAsync<ProviderUnavailableException>(
            () => provider.GetDimensionAsync(CancellationToken.None));

        Assert.Equal(1, facade.CallCount);
        Assert.IsType<HttpRequestException>(first.InnerException);
        Assert.IsType<HttpRequestException>(second.InnerException);
    }

    [Fact]
    public async Task GetDimensionAsync_OnFailure_DoesNotRetry_PollyBypass()
    {
        // A transient HttpRequestException is exactly what the resilience pipeline
        // would retry up to 3 times (4 total invocations). The dimension probe
        // bypasses Polly, so we expect a single call.
        var facade = new FakeProbeFacade
        {
            Custom = (_, _) => throw new HttpRequestException("transient"),
        };
        var provider = CreateProvider(facade);

        await Assert.ThrowsAsync<ProviderUnavailableException>(
            () => provider.GetDimensionAsync(CancellationToken.None));

        Assert.Equal(1, facade.CallCount);
    }

    [Fact]
    public async Task GetDimensionAsync_WhenSdkThrowsClientResultException_TranslatesToProviderUnavailable()
    {
        var facade = new FakeProbeFacade
        {
            Custom = (_, _) => throw new ClientResultException("upstream rejected"),
        };
        var provider = CreateProvider(facade);

        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(
            () => provider.GetDimensionAsync(CancellationToken.None));

        Assert.IsType<ClientResultException>(ex.InnerException);
    }

    [Fact]
    public void DiContainer_ResolvesEmbeddingClientAndDimensionProvider_ToSameSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMinerva(opt =>
        {
            opt.ConnectionString = "Host=localhost";
            opt.Embedding = new ProviderOptions
            {
                BaseUrl = "http://localhost",
                Model = "test-model",
            };
        });

        using var sp = services.BuildServiceProvider();
        var asClient = sp.GetRequiredService<IEmbeddingClient>();
        var asDimension = sp.GetRequiredService<IEmbeddingDimensionProvider>();

        Assert.Same(asClient, asDimension);
    }

    private static OpenAICompatibleEmbeddingProvider CreateProvider(IEmbeddingProbeFacade facade)
    {
        var endpoint = new Uri("http://localhost");
        var openAi = new OpenAI.OpenAIClient(
            new ApiKeyCredential("test"),
            new OpenAI.OpenAIClientOptions { Endpoint = endpoint });
        var embeddingClient = openAi.GetEmbeddingClient("test-model");
        var rateLimiter = new RateLimiter(concurrency: 1, requestsPerMinute: null);
        return new OpenAICompatibleEmbeddingProvider(
            embeddingClient, rateLimiter, "test-model", endpoint, facade);
    }

    private sealed class FakeProbeFacade : IEmbeddingProbeFacade
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public int Dimension { get; init; } = 1536;
        public Func<string, CancellationToken, Task<int>>? Custom { get; init; }

        public Task<int> EmbedAndCountDimensionsAsync(string input, CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            return Custom is not null ? Custom(input, ct) : Task.FromResult(Dimension);
        }
    }
}
