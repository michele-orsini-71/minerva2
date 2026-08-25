namespace Minerva.Search;

interface IReranker
{
    Task<IReadOnlyList<ScoredChunk>> Rank(
        string query, IReadOnlyList<ScoredChunk> candidates, CancellationToken cancellationToken);
}
