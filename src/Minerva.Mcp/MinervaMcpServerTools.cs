using ModelContextProtocol.Server;
using System.ComponentModel;
using Minerva;
using System.Globalization;
using System.Text.Json;
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
        var collections = await engine.QueryListCollections();
        return collections
            .Select(c => new CollectionInfo(
                c.Name, c.Description, c.EmbeddingModel, c.EmbeddingDimension,
                c.CreatedAt, c.LastUpdatedAt))
            .ToList();
    }
}
 


