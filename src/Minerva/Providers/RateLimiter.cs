using System.Collections.Concurrent;

namespace Minerva.Providers;

public sealed class RateLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int? _requestsPerMinute;
    private readonly ConcurrentQueue<DateTimeOffset> _requestTimes = new();
    private readonly object _pruneLock = new();
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    public RateLimiter(int concurrency, int? requestsPerMinute)
    {
        _semaphore = new SemaphoreSlim(concurrency, concurrency);
        _requestsPerMinute = requestsPerMinute;
    }

    public async Task AcquireAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);

        if (_requestsPerMinute is not { } rpm)
            return;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;

            lock (_pruneLock)
            {
                Prune(now);
                if (_requestTimes.Count < rpm)
                {
                    _requestTimes.Enqueue(now);
                    return;
                }
            }

            await Task.Delay(50, ct);
        }
    }

    public void Release()
    {
        _semaphore.Release();
    }

    private void Prune(DateTimeOffset now)
    {
        while (_requestTimes.TryPeek(out var earliest) && now - earliest >= Window)
        {
            _requestTimes.TryDequeue(out _);
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
