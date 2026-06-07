using Minerva.Models;
using Minerva.Search.Bench.Common;
using Minerva.Search.Bench.Sweep;

namespace Minerva.Tests.Search.Bench;


[Trait("Category", "Bench")]
public class SweepRunLoopTests
{
    private sealed class FakeSearchEngine(Func<string, IReadOnlyList<SearchResult>> onSearch)
        : ISearchEngine
    {
        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query, string collectionName,
            SearchOverrides? overrides = null, CancellationToken ct = default)
            => Task.FromResult(onSearch(query));

        public Task<bool> SourceIdExistsAsync(
            string collectionName, string sourceId, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private static SearchResult Hit(string sourceId)
        => new("chunk", sourceId, "col", "content", 1.0);

    private static ParsedEntry Entry(string id, string query, string gold)
        => new(1, id, query, [gold]);

    private static IReadOnlyList<Cell> Cells(params object[] topKValues)
        => CellEnumerator.Enumerate(
            new Dictionary<string, List<object>> { ["top_k"] = topKValues.ToList() });

    [Fact]
    public async Task ResultCount_IsEntriesTimesCells()
    {
        var engine = new FakeSearchEngine(_ => [Hit("a")]);
        var entries = new[] { Entry("q1", "x", "a"), Entry("q2", "y", "b") };
        var cells = Cells(10L, 20L); // 2 cells

        var results = await SweepRunLoop.RunAsync(engine, "col", entries, cells);

        Assert.Equal(4, results.Count); // 2 entries x 2 cells
    }

    [Fact]
    public async Task ThrowingQuery_ProducesErrorRecord_WithoutAborting()
    {
        var engine = new FakeSearchEngine(query =>
            query == "boom"
                ? throw new InvalidOperationException("search failed")
                : [Hit("a")]);
        var entries = new[] { Entry("q1", "ok", "a"), Entry("q2", "boom", "b") };

        var results = await SweepRunLoop.RunAsync(engine, "col", entries, Cells(10L));

        Assert.Equal(2, results.Count);

        var ok = results.Single(r => r.QueryId == "q1");
        Assert.NotNull(ok.Scores);
        Assert.Null(ok.Error);

        var failed = results.Single(r => r.QueryId == "q2");
        Assert.Null(failed.Scores);
        Assert.Null(failed.Hits);
        Assert.Contains("search failed", failed.Error);
    }

    [Fact]
    public async Task Metrics_AreComputedFromReturnedSourceIds()
    {
        var engine = new FakeSearchEngine(_ => [Hit("gold"), Hit("other")]);
        var entries = new[] { Entry("q1", "x", "gold") };

        var results = await SweepRunLoop.RunAsync(engine, "col", entries, Cells(10L));

        var result = Assert.Single(results);
        Assert.NotNull(result.Scores);
        Assert.Equal(1.0, result.Scores!.RecallAt5);
        Assert.Equal(1.0, result.Scores.MrrAt10);

        Assert.NotNull(result.Hits);
        Assert.Equal(2, result.Hits!.Count);
        Assert.Equal(1, result.Hits[0].Rank);
        Assert.Equal("gold", result.Hits[0].SourceId);
        Assert.True(result.Hits[0].GoldHit);
        Assert.False(result.Hits[1].GoldHit);
    }
}
