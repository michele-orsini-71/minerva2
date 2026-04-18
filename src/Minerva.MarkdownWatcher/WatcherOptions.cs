namespace Minerva.MarkdownWatcher;

public class WatcherOptions
{
    public string RootPath { get; set; } = string.Empty;
    public string CollectionName { get; set; } = string.Empty;
    public string FilePattern { get; set; } = "*.md";
    public int DebounceMs { get; set; } = 500;
    public string[] ExcludeDirectories { get; set; } = [".obsidian", ".trash", ".git"];
}
