using Minerva.Models;

namespace Minerva.MarkdownWatcher;

public interface IMarkdownScanner
{
    IReadOnlyList<string> ScanFiles();
    Document ReadFile(string filePath);
    bool IsExcluded(string filePath);
    string DeriveSourceId(string filePath);
}
