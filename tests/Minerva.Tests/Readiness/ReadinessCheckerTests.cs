using Microsoft.Extensions.Logging.Abstractions;
using Minerva.Readiness;

namespace Minerva.Tests.Readiness;

[Trait("Category", "Readiness")]
public class ReadinessCheckerTests
{
    [Fact]
    public async Task CheckReadinessAsync_EmptyRegistry_IsReady()
    {
        var marker = new ReadinessProbeMarker();
        var checker = new ReadinessChecker(
            checks: [],
            marker: marker,
            logger: NullLogger<ReadinessChecker>.Instance);

        var report = await checker.CheckReadinessAsync();

        Assert.True(report.IsReady);
        Assert.Empty(report.Results);
        Assert.True(marker.Probed);
    }

    [Fact]
    public async Task CheckReadinessAsync_AllPass_IsReadyAndPreservesRegistrationOrder()
    {
        var marker = new ReadinessProbeMarker();
        var checker = new ReadinessChecker(
            checks:
            [
                new StubCheck("first", ReadinessCategory.Storage, passed: true),
                new StubCheck("second", ReadinessCategory.Embedding, passed: true),
                new StubCheck("third", ReadinessCategory.Llm, passed: true),
            ],
            marker: marker,
            logger: NullLogger<ReadinessChecker>.Instance);

        var report = await checker.CheckReadinessAsync();

        Assert.True(report.IsReady);
        Assert.Equal(["first", "second", "third"], report.Results.Select(r => r.Name));
    }

    [Fact]
    public async Task CheckReadinessAsync_OneFailure_IsNotReady()
    {
        var checker = new ReadinessChecker(
            checks:
            [
                new StubCheck("ok", ReadinessCategory.Storage, passed: true),
                new StubCheck("bad", ReadinessCategory.Embedding, passed: false),
            ],
            marker: new ReadinessProbeMarker(),
            logger: NullLogger<ReadinessChecker>.Instance);

        var report = await checker.CheckReadinessAsync();

        Assert.False(report.IsReady);
    }

    [Fact]
    public async Task CheckReadinessAsync_CheckThrows_IsTranslatedToFailedResultNotPropagated()
    {
        var checker = new ReadinessChecker(
            checks: [new ThrowingCheck("boom", new InvalidOperationException("connection refused; Password=hunter2"))],
            marker: new ReadinessProbeMarker(),
            logger: NullLogger<ReadinessChecker>.Instance);

        var report = await checker.CheckReadinessAsync();

        var result = Assert.Single(report.Results);
        Assert.False(result.Passed);
        Assert.Equal("MINERVA.READINESS.UNHANDLED", result.Code);
        Assert.NotNull(result.Message);
        Assert.DoesNotContain("hunter2", result.Message);
    }

    [Fact]
    public async Task CheckReadinessAsync_CheckExceedsTimeout_ProducesTimeoutResult()
    {
        var checker = new ReadinessChecker(
            checks: [new SlowCheck("slow", TimeSpan.FromMilliseconds(50))],
            marker: new ReadinessProbeMarker(),
            logger: NullLogger<ReadinessChecker>.Instance);

        var report = await checker.CheckReadinessAsync();

        var result = Assert.Single(report.Results);
        Assert.False(result.Passed);
        Assert.Equal("MINERVA.READINESS.TIMEOUT", result.Code);
    }

    [Fact]
    public async Task CheckReadinessAsync_FlipsMarkerAfterRun()
    {
        var marker = new ReadinessProbeMarker();
        Assert.False(marker.Probed);

        var checker = new ReadinessChecker(
            checks: [new StubCheck("x", ReadinessCategory.Storage, passed: true)],
            marker: marker,
            logger: NullLogger<ReadinessChecker>.Instance);

        await checker.CheckReadinessAsync();

        Assert.True(marker.Probed);
    }

    [Fact]
    public void MarkProbed_IsIdempotent()
    {
        var marker = new ReadinessProbeMarker();
        marker.MarkProbed();
        marker.MarkProbed();
        Assert.True(marker.Probed);
    }

    private sealed class StubCheck : IReadinessCheck
    {
        public StubCheck(string name, ReadinessCategory category, bool passed)
        {
            Name = name;
            Category = category;
            _passed = passed;
        }

        private readonly bool _passed;
        public string Name { get; }
        public ReadinessCategory Category { get; }

        public Task<ReadinessCheckResult> RunAsync(CancellationToken ct) =>
            Task.FromResult(new ReadinessCheckResult(
                Name, Category, _passed, _passed ? "OK" : "MINERVA.STUB.FAIL", null, null));
    }

    private sealed class ThrowingCheck : IReadinessCheck
    {
        private readonly Exception _ex;

        public ThrowingCheck(string name, Exception ex)
        {
            Name = name;
            _ex = ex;
        }

        public string Name { get; }
        public ReadinessCategory Category => ReadinessCategory.Storage;

        public Task<ReadinessCheckResult> RunAsync(CancellationToken ct) => throw _ex;
    }

    private sealed class SlowCheck : IReadinessCheck, IReadinessCheckTimeout
    {
        public SlowCheck(string name, TimeSpan timeout)
        {
            Name = name;
            Timeout = timeout;
        }

        public string Name { get; }
        public ReadinessCategory Category => ReadinessCategory.Storage;
        public TimeSpan Timeout { get; }

        public async Task<ReadinessCheckResult> RunAsync(CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new ReadinessCheckResult(Name, Category, true, "OK", null, null);
        }
    }
}
