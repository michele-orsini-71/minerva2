using Minerva.Search;

namespace Minerva.IntegrationTests.TestSupport;

// The search pipeline requires an IReranker; E2E tests that don't exercise
// reranking use this passthrough (candidates returned unchanged).
internal sealed class NoopReranker : IReranker
{
    public Task<IReadOnlyList<ScoredChunk>> Rank(
        string query, IReadOnlyList<ScoredChunk> candidates, CancellationToken cancellationToken) =>
        Task.FromResult(candidates);
}
