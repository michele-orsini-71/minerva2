namespace Minerva.MarkdownWatcher;

public class WatcherOptions
{
    public required string RootPath { get; set; }
    public required string CollectionName { get; set; }
    public string FilePattern { get; set; } = "*.md";
    public int DebounceMs { get; set; } = 500;
    public string[] ExcludeDirectories { get; set; } = [".obsidian", ".trash", ".git"];
}
