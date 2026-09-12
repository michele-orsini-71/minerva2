using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Minerva;
using Minerva.Exceptions;
using Minerva.Models;

public sealed record CollectionInfo(
    string Name,
    string? Description,
    string EmbeddingModel,
    int EmbeddingDimension,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt);

[McpServerToolType]
public static class MinervaMcpServerTools
{
    [McpServerTool(Name = "list_collections"), Description("Get list of available collections.")]
    public static async Task<IReadOnlyList<CollectionInfo>> GetCollections(ISearchEngine engine)
    {
        var collections = await engine.QueryListCollectionsAsync();
        return collections
            .Select(c => new CollectionInfo(
                c.Name, c.Description, c.EmbeddingModel, c.EmbeddingDimension,
                c.CreatedAt, c.LastUpdatedAt))
            .ToList();
    }

    [McpServerTool(Name = "search"), Description("Hybrid search over a collection. Returns the best matching chunks.")]
    public static async Task<IReadOnlyList<SearchHit>> Search(
        ISearchEngine engine,
        [Description("The search query.")] string query,
        [Description("Name of the collection to search.")] string collection,
        [Description("Maximum number of hits to return.")] int? topK = null,
        CancellationToken ct = default)
    {
        var overrides = new SearchOverrides { TopK = topK, ExpandContext = false };
        var results = await Guard(() => engine.SearchAsync(query, collection, overrides, ct));
        return results
            .Select(r => new SearchHit(r.ChunkId, r.SourceId, r.ChunkIndex, r.Score, r.Content))
            .ToList();
    }

    [McpServerTool(Name = "get_source"), Description("Get the full text of a source as it was ingested.")]
    public static async Task<SourceText> GetSource(
        ISearchEngine engine,
        [Description("Name of the collection.")] string collection,
        [Description("Id of the source, as returned by search.")] string sourceId,
        CancellationToken ct = default)
    {
        return await Guard(() => engine.GetSourceAsync(collection, sourceId, ct))
            ?? throw new McpException($"Source '{sourceId}' not found in collection '{collection}'.");
    }

    [McpServerTool(Name = "get_source_info"), Description(
        "Get title, chunk count and character count of a source, without its text.")]
    public static async Task<SourceInfo> GetSourceInfo(
        ISearchEngine engine,
        [Description("Name of the collection.")] string collection,
        [Description("Id of the source, as returned by search.")] string sourceId,
        CancellationToken ct = default)
    {
        return await Guard(() => engine.GetSourceInfoAsync(collection, sourceId, ct))
            ?? throw new McpException($"Source '{sourceId}' not found in collection '{collection}'.");
    }

    [McpServerTool(Name = "expand_chunk"), Description(
        "Get a chunk together with its neighbours in the same source: chunks with index " +
        "in [chunkIndex - window, chunkIndex + window], in document order. Adjacent chunks " +
        "overlap slightly. Use it when a search hit is cut at its start or end.")]
    public static async Task<IReadOnlyList<ChunkText>> ExpandChunk(
        ISearchEngine engine,
        [Description("Name of the collection.")] string collection,
        [Description("Id of the source, as returned by search.")] string sourceId,
        [Description("Index of the chunk to expand, as returned by search.")] int chunkIndex,
        [Description("Number of neighbours on each side.")] int window = 1,
        CancellationToken ct = default)
    {
        if (window < 1)
            throw new McpException($"window must be at least 1 (got {window}).");

        var chunks = await Guard(
            () => engine.GetChunkWindowAsync(collection, sourceId, chunkIndex, window, ct));
        if (chunks.Count == 0)
            throw new McpException(
                $"No chunk found at index {chunkIndex} of source '{sourceId}' in collection '{collection}'.");
        return chunks;
    }

    // The SDK forwards only McpException messages to the client; any other exception
    // is logged server-side and replaced by "An error occurred invoking '<tool>'".
    // Argument and configuration errors (unknown collection, bad TopK, ...) are the
    // caller's mistake, so their message must reach the caller. Storage and provider
    // failures stay hidden on purpose.
    private static async Task<T> Guard<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is ConfigurationException or ArgumentException)
        {
            throw new McpException(ex.Message, ex);
        }
    }
}

public sealed record SearchHit(
    string ChunkId,
    string SourceId,
    int ChunkIndex,
    double Score,
    string Content);
 


