using Minerva.Providers;

namespace Minerva.Tests.Providers;

[Trait("Category", "Providers")]
public class RateLimiterTests
{
    [Fact]
    public async Task AcquireAsync_RespectsMaxConcurrency()
    {
        using var limiter = new RateLimiter(concurrency: 2, requestsPerMinute: null);
        var inFlight = 0;
        var maxObserved = 0;
        var lockObj = new object();

        var tasks = Enumerable.Range(0, 6).Select(async _ =>
        {
            await limiter.AcquireAsync();
            try
            {
                lock (lockObj)
                {
                    inFlight++;
                    if (inFlight > maxObserved) maxObserved = inFlight;
                }

                await Task.Delay(50);
            }
            finally
            {
                lock (lockObj) inFlight--;
                limiter.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        Assert.True(maxObserved <= 2, $"Expected max 2 concurrent, but observed {maxObserved}");
    }

    [Fact]
    public async Task AcquireAsync_WithNullRpm_DoesNotThrottle()
    {
        using var limiter = new RateLimiter(concurrency: 10, requestsPerMinute: null);

        // Should complete near-instantly — no RPM gating
        for (int i = 0; i < 20; i++)
        {
            await limiter.AcquireAsync();
            limiter.Release();
        }
    }

    [Fact]
    public async Task AcquireAsync_RespectsRpmLimit()
    {
        using var limiter = new RateLimiter(concurrency: 10, requestsPerMinute: 3);

        // First 3 should acquire immediately
        for (int i = 0; i < 3; i++)
        {
            await limiter.AcquireAsync();
            limiter.Release();
        }

        // 4th acquire should block until the sliding window clears
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.AcquireAsync(cts.Token));
    }

    [Fact]
    public async Task AcquireAsync_CancellationToken_CancelsWait()
    {
        using var limiter = new RateLimiter(concurrency: 1, requestsPerMinute: null);

        // Hold the single slot
        await limiter.AcquireAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => limiter.AcquireAsync(cts.Token));

        limiter.Release();
    }

    [Fact]
    public async Task Dispose_ReleasesResources()
    {
        var limiter = new RateLimiter(concurrency: 1, requestsPerMinute: null);
        limiter.Dispose();

        // After dispose, SemaphoreSlim.WaitAsync should throw ObjectDisposedException
        await Assert.ThrowsAsync<ObjectDisposedException>(() => limiter.AcquireAsync());
    }
}
