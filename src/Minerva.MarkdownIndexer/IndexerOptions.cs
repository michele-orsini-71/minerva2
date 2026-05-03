namespace Minerva.MarkdownIndexer;

public class IndexerOptions
{
    public required string RootPath { get; set; }
    public required string CollectionName { get; set; }
    public string FilePattern { get; set; } = "*.md";
    public string[] ExcludeDirectories { get; set; } = [];
}
