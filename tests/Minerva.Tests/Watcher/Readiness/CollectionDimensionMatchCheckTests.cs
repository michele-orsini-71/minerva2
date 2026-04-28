using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Ingestion;
using Minerva.MarkdownWatcher;
using Minerva.MarkdownWatcher.Readiness;
using Minerva.Models;

namespace Minerva.Tests.Watcher.Readiness;

[Trait("Category", "Readiness")]
public class CollectionDimensionMatchCheckTests
{
    [Fact]
    public async Task ShortCircuits_WhenStorageOrEmbeddingNotConfigured()
    {
        var check = Build(
            connectionString: null,
            embedding: null,
            dimensionProvider: null,
            probe: null);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.CLIENT.DIMENSION_NOT_CONFIGURED", result.Code);
    }

    [Fact]
    public async Task Branch1_PassesWithSkip_WhenEmbedderUnavailable()
    {
        var check = Build(
            connectionString: "Host=db;Username=u;Password=p;Database=d",
            embedding: SomeEmbedding(),
            dimensionProvider: new FakeDimProvider(_ => throw new ProviderUnavailableException("embedder down")),
            probe: new FakeProbe(_ => Task.FromResult<int?>(1024)));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.CLIENT.DIMENSION_SKIPPED", result.Code);
        Assert.Contains("skipped", result.Message);
    }

    [Fact]
    public async Task Branch2_PassesTrivially_WhenCollectionsTableMissing()
    {
        // The probe handles 42P01 internally and returns null; from the check's
        // perspective this is indistinguishable from "no row" — both pass trivially.
        var check = Build(
            connectionString: "Host=db;Username=u;Password=p;Database=d",
            embedding: SomeEmbedding(),
            dimensionProvider: new FakeDimProvider(_ => Task.FromResult(768)),
            probe: new FakeProbe(_ => Task.FromResult<int?>(null)));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.CLIENT.DIMENSION_OK", result.Code);
    }

    [Fact]
    public async Task Branch3_PassesTrivially_WhenNoRowMatchesCollectionName()
    {
        var check = Build(
            connectionString: "Host=db;Username=u;Password=p;Database=d",
            embedding: SomeEmbedding(),
            dimensionProvider: new FakeDimProvider(_ => Task.FromResult(768)),
            probe: new FakeProbe(_ => Task.FromResult<int?>(null)));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.CLIENT.DIMENSION_OK", result.Code);
    }

    [Fact]
    public async Task Branch4_Fails_WhenStoredDimensionDiffersFromProbed()
    {
        var check = Build(
            connectionString: "Host=db;Username=u;Password=p;Database=d",
            embedding: SomeEmbedding(),
            dimensionProvider: new FakeDimProvider(_ => Task.FromResult(768)),
            probe: new FakeProbe(_ => Task.FromResult<int?>(1024)),
            collectionName: "my-notes");

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.CLIENT.DIMENSION_MISMATCH", result.Code);
        Assert.Contains("768", result.Remediation);
        Assert.Contains("1024", result.Remediation);
        Assert.Contains("DELETE FROM collections WHERE name='my-notes'", result.Remediation);
    }

    [Fact]
    public async Task Passes_WhenStoredDimensionMatchesProbed()
    {
        var check = Build(
            connectionString: "Host=db;Username=u;Password=p;Database=d",
            embedding: SomeEmbedding(),
            dimensionProvider: new FakeDimProvider(_ => Task.FromResult(1024)),
            probe: new FakeProbe(_ => Task.FromResult<int?>(1024)));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.CLIENT.DIMENSION_OK", result.Code);
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var check = Build(
            connectionString: "Host=db;Username=u;Password=p;Database=d",
            embedding: SomeEmbedding(),
            dimensionProvider: new FakeDimProvider(ct => Task.FromCanceled<int>(ct)),
            probe: new FakeProbe(_ => Task.FromResult<int?>(null)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => check.RunAsync(cts.Token));
    }

    private static ProviderOptions SomeEmbedding() => new()
    {
        BaseUrl = "http://embed.example",
        Model = "text-embed-3",
    };

    private static CollectionDimensionMatchCheck Build(
        string? connectionString,
        ProviderOptions? embedding,
        IEmbeddingDimensionProvider? dimensionProvider,
        ICollectionDimensionProbe? probe,
        string collectionName = "my-notes") =>
        new(
            new MinervaOptions { ConnectionString = connectionString, Embedding = embedding },
            new WatcherOptions { RootPath = "/x", CollectionName = collectionName },
            dimensionProvider,
            probe,
            NullLogger<CollectionDimensionMatchCheck>.Instance);

    private sealed class FakeDimProvider : IEmbeddingDimensionProvider
    {
        private readonly Func<CancellationToken, Task<int>> _impl;
        public FakeDimProvider(Func<CancellationToken, Task<int>> impl) => _impl = impl;
        public Task<int> GetDimensionAsync(CancellationToken ct = default) => _impl(ct);
    }

    private sealed class FakeProbe : ICollectionDimensionProbe
    {
        private readonly Func<CancellationToken, Task<int?>> _impl;
        public FakeProbe(Func<CancellationToken, Task<int?>> impl) => _impl = impl;
        public Task<int?> TryGetStoredDimensionAsync(string collectionName, CancellationToken ct) => _impl(ct);
    }
}
