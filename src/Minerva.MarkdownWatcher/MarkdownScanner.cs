using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Minerva.Models;

namespace Minerva.MarkdownWatcher;

public partial class MarkdownScanner : IMarkdownScanner
{
    private static readonly Regex ImageRegex = MarkdownImageRegex();

    private readonly WatcherOptions _options;

    public MarkdownScanner(IOptions<WatcherOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<string> ScanFiles()
    {
        if (!Directory.Exists(_options.RootPath))
            return Array.Empty<string>();

        var excludeSet = new HashSet<string>(_options.ExcludeDirectories, StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();

        foreach (var path in Directory.EnumerateFiles(
            _options.RootPath, _options.FilePattern, SearchOption.AllDirectories))
        {
            if (IsExcluded(path, excludeSet))
                continue;
            results.Add(path);
        }

        return results;
    }

    public Document ReadFile(string filePath)
    {
        var raw = File.ReadAllText(filePath);
        var (frontmatter, body) = SplitFrontmatter(raw);
        var metadata = ParseFrontmatter(frontmatter);

        var title = metadata.TryGetValue("title", out var t) && t is string ts && !string.IsNullOrWhiteSpace(ts)
            ? ts
            : Path.GetFileNameWithoutExtension(filePath);

        var sourceId = DeriveSourceId(filePath);
        var attachments = ExtractImages(body);

        return new Document(
            SourceId: sourceId,
            Title: title,
            Text: body,
            Metadata: metadata.Count > 0 ? metadata : null,
            Attachments: attachments.Count > 0 ? attachments : null);
    }

    public bool IsExcluded(string filePath)
    {
        var excludeSet = new HashSet<string>(_options.ExcludeDirectories, StringComparer.OrdinalIgnoreCase);
        return IsExcluded(filePath, excludeSet);
    }

    public string DeriveSourceId(string filePath)
    {
        var relative = Path.GetRelativePath(_options.RootPath, filePath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private bool IsExcluded(string filePath, HashSet<string> excludeSet)
    {
        var relative = Path.GetRelativePath(_options.RootPath, filePath);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (excludeSet.Contains(segment))
                return true;
        }
        return false;
    }

    internal static (string? frontmatter, string body) SplitFrontmatter(string raw)
    {
        if (!raw.StartsWith("---", StringComparison.Ordinal))
            return (null, raw);

        // Match only the leading fence; the line immediately after `---` must exist.
        var afterOpening = raw.IndexOf('\n', 3);
        if (afterOpening < 0)
            return (null, raw);

        // Find closing `---` on its own line.
        var searchFrom = afterOpening + 1;
        while (searchFrom < raw.Length)
        {
            var lineEnd = raw.IndexOf('\n', searchFrom);
            var line = lineEnd < 0
                ? raw[searchFrom..]
                : raw[searchFrom..lineEnd];
            if (line.TrimEnd('\r').Trim() == "---")
            {
                var frontmatter = raw[(afterOpening + 1)..searchFrom];
                var body = lineEnd < 0 ? string.Empty : raw[(lineEnd + 1)..];
                return (frontmatter, body);
            }
            if (lineEnd < 0) break;
            searchFrom = lineEnd + 1;
        }

        return (null, raw);
    }

    internal static Dictionary<string, object> ParseFrontmatter(string? frontmatter)
    {
        var metadata = new Dictionary<string, object>();
        if (string.IsNullOrWhiteSpace(frontmatter))
            return metadata;

        foreach (var rawLine in frontmatter.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Length == 0)
                continue;

            metadata[key] = ParseValue(value);
        }

        return metadata;
    }

    private static object ParseValue(string value)
    {
        if (value.Length == 0) return string.Empty;

        if ((value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2) ||
            (value.StartsWith('\'') && value.EndsWith('\'') && value.Length >= 2))
        {
            return value[1..^1];
        }

        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            var inner = value[1..^1];
            return inner.Split(',')
                .Select(p => p.Trim().Trim('"', '\''))
                .Where(p => p.Length > 0)
                .ToArray();
        }

        if (bool.TryParse(value, out var b)) return b;
        if (long.TryParse(value, out var l)) return l;
        if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;

        return value;
    }

    internal static Dictionary<string, AttachmentDescription> ExtractImages(string body)
    {
        var attachments = new Dictionary<string, AttachmentDescription>();
        foreach (Match match in ImageRegex.Matches(body))
        {
            var key = match.Value;
            if (attachments.ContainsKey(key)) continue;

            var alt = match.Groups[1].Value.Trim();
            var path = match.Groups[2].Value.Trim();
            var description = alt.Length > 0 ? alt : Path.GetFileNameWithoutExtension(path);

            attachments[key] = new AttachmentDescription(
                Description: description,
                SourcePath: path);
        }
        return attachments;
    }

    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownImageRegex();
}
