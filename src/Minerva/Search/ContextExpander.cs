using Minerva.Models;

namespace Minerva.Search;

public class ContextExpander
{
    private readonly IChunkQuery _chunkQuery;

    public ContextExpander(IChunkQuery chunkQuery)
    {
        _chunkQuery = chunkQuery;
    }

    public async Task<IReadOnlyList<SearchResult>> ExpandAsync(
        IReadOnlyList<ScoredChunk> results,
        CancellationToken ct = default)
    {
        if (results.Count == 0)
            return [];

        var adjacentIds = new HashSet<string>();
        foreach (var r in results)
        {
            if (r.Chunk.PrevChunkId is not null) adjacentIds.Add(r.Chunk.PrevChunkId);
            if (r.Chunk.NextChunkId is not null) adjacentIds.Add(r.Chunk.NextChunkId);
        }

        Dictionary<string, string> contentById;
        if (adjacentIds.Count == 0)
        {
            contentById = new Dictionary<string, string>();
        }
        else
        {
            var adjacent = await _chunkQuery.GetAdjacentChunksAsync(
                adjacentIds.ToArray(), ct);
            contentById = adjacent.ToDictionary(c => c.Id, c => c.Content);
        }

        var expanded = new List<SearchResult>(results.Count);
        foreach (var r in results)
        {
            string? before = r.Chunk.PrevChunkId is { } prev
                && contentById.TryGetValue(prev, out var prevContent)
                    ? prevContent : null;
            string? after = r.Chunk.NextChunkId is { } next
                && contentById.TryGetValue(next, out var nextContent)
                    ? nextContent : null;

            expanded.Add(new SearchResult(
                ChunkId: r.Chunk.Id,
                SourceId: r.Chunk.SourceId,
                CollectionName: r.Chunk.CollectionName,
                Content: r.Chunk.Content,
                Score: r.Score,
                Metadata: r.Chunk.Metadata,
                ContextBefore: before,
                ContextAfter: after));
        }

        return expanded;
    }
}
