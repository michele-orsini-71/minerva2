using Minerva.Watcher;

namespace Minerva.Tests.Watcher;

[Trait("Category", "Watcher")]
public class PathDebouncerTests
{
    [Fact]
    public async Task RapidEventsForSamePath_CoalesceIntoSingleInvocation()
    {
        var invocations = new List<string>();
        using var debouncer = new PathDebouncer(
            TimeSpan.FromMilliseconds(50),
            (path, _) => { lock (invocations) invocations.Add(path); return Task.CompletedTask; });

        for (int i = 0; i < 10; i++)
        {
            debouncer.Schedule("/a");
            await Task.Delay(5);
        }

        await Task.Delay(200);

        Assert.Single(invocations);
        Assert.Equal("/a", invocations[0]);
    }

    [Fact]
    public async Task EventsForDifferentPaths_AreProcessedIndependently()
    {
        var invocations = new List<string>();
        using var debouncer = new PathDebouncer(
            TimeSpan.FromMilliseconds(30),
            (path, _) => { lock (invocations) invocations.Add(path); return Task.CompletedTask; });

        debouncer.Schedule("/a");
        debouncer.Schedule("/b");
        debouncer.Schedule("/c");

        await Task.Delay(150);

        lock (invocations)
        {
            Assert.Equal(3, invocations.Count);
            Assert.Contains("/a", invocations);
            Assert.Contains("/b", invocations);
            Assert.Contains("/c", invocations);
        }
    }

    [Fact]
    public async Task SchedulingAfterDelayCompletes_InvokesAgain()
    {
        var count = 0;
        using var debouncer = new PathDebouncer(
            TimeSpan.FromMilliseconds(30),
            (_, _) => { Interlocked.Increment(ref count); return Task.CompletedTask; });

        debouncer.Schedule("/a");
        await Task.Delay(100);
        debouncer.Schedule("/a");
        await Task.Delay(100);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Dispose_CancelsPendingInvocations()
    {
        var invoked = false;
        var debouncer = new PathDebouncer(
            TimeSpan.FromMilliseconds(100),
            (_, _) => { invoked = true; return Task.CompletedTask; });

        debouncer.Schedule("/a");
        debouncer.Dispose();
        await Task.Delay(200);

        Assert.False(invoked);
    }
}
