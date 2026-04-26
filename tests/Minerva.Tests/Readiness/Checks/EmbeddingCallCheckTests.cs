using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Readiness.Checks;

namespace Minerva.Tests.Readiness.Checks;

[Trait("Category", "Readiness")]
public class EmbeddingCallCheckTests
{
    [Fact]
    public async Task ShortCircuits_WhenEmbeddingNull()
    {
        var check = Build(embedding: null, dimensionProvider: null);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.EMBEDDING.NOT_CONFIGURED", result.Code);
    }

    [Fact]
    public async Task Passes_WhenProbeReturnsDimension()
    {
        var check = Build(SomeEmbedding(), new FakeDimProvider(_ => Task.FromResult(1536)));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.EMBEDDING.OK", result.Code);
    }

    [Fact]
    public async Task Fails_WhenProviderUnavailable()
    {
        var check = Build(SomeEmbedding(),
            new FakeDimProvider(_ => throw new ProviderUnavailableException("upstream Bearer abcdef rejected")));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.EMBEDDING.UNAVAILABLE", result.Code);
        Assert.Contains("http://embed.example", result.Remediation);
        Assert.Contains("text-embed-3", result.Remediation);
        Assert.NotNull(result.Message);
        Assert.DoesNotContain("abcdef", result.Message);
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var check = Build(SomeEmbedding(),
            new FakeDimProvider(ct => Task.FromCanceled<int>(ct)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => check.RunAsync(cts.Token));
    }

    private static ProviderOptions SomeEmbedding() => new()
    {
        BaseUrl = "http://embed.example",
        Model = "text-embed-3",
    };

    private static EmbeddingCallCheck Build(ProviderOptions? embedding, IEmbeddingDimensionProvider? dimensionProvider)
    {
        var options = Options.Create(new MinervaOptions { Embedding = embedding });
        var services = new ServiceCollection();
        if (dimensionProvider is not null)
            services.AddSingleton(dimensionProvider);
        var sp = services.BuildServiceProvider();
        return new EmbeddingCallCheck(options, sp, NullLogger<EmbeddingCallCheck>.Instance);
    }

    private sealed class FakeDimProvider : IEmbeddingDimensionProvider
    {
        private readonly Func<CancellationToken, Task<int>> _impl;
        public FakeDimProvider(Func<CancellationToken, Task<int>> impl) => _impl = impl;
        public Task<int> GetDimensionAsync(CancellationToken ct = default) => _impl(ct);
    }
}
