using System.Collections.Concurrent;

namespace Minerva.MarkdownWatcher;

/// <summary>
/// Coalesces rapid repeated events for the same path into a single delayed invocation.
/// Each new call for a given path cancels the previous pending invocation and restarts the timer.
/// </summary>
public sealed class PathDebouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Func<string, CancellationToken, Task> _handler;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new();
    private readonly CancellationTokenSource _shutdown = new();

    public PathDebouncer(TimeSpan delay, Func<string, CancellationToken, Task> handler)
    {
        _delay = delay;
        _handler = handler;
    }

    public void Schedule(string path)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var previous = _pending.AddOrUpdate(path, cts, (_, existing) =>
        {
            existing.Cancel();
            existing.Dispose();
            return cts;
        });

        _ = RunAfterDelay(path, cts);
    }

    private async Task RunAfterDelay(string path, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(_delay, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_pending.TryRemove(new KeyValuePair<string, CancellationTokenSource>(path, cts)))
        {
            try
            {
                await _handler(path, _shutdown.Token).ConfigureAwait(false);
            }
            finally
            {
                cts.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        foreach (var kvp in _pending)
        {
            try { kvp.Value.Cancel(); kvp.Value.Dispose(); } catch { /* best effort */ }
        }
        _pending.Clear();
        _shutdown.Dispose();
    }
}
