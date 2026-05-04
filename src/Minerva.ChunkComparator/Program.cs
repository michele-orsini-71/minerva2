using System.Text;
using Microsoft.Extensions.Configuration;
using Minerva.Ingestion;
using Minerva.Models;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var rootPath = config["Indexer:RootPath"]
    ?? throw new InvalidOperationException("Indexer:RootPath is required.");
var filePattern = config["Indexer:FilePattern"] ?? "*.md";
var excludeDirs = config.GetSection("Indexer:ExcludeDirectories").Get<string[]>() ?? [];

var chunkingOptions = new ChunkingOptions();
config.GetSection("Minerva:Chunking").Bind(chunkingOptions);

var outputDir = config["Comparator:OutputDir"] ?? "./chunk-comparison";

if (!Directory.Exists(rootPath))
    throw new DirectoryNotFoundException($"RootPath does not exist: {rootPath}");

var customDir = Path.Combine(outputDir, "custom");
var skDir = Path.Combine(outputDir, "microsoft");
Directory.CreateDirectory(customDir);
Directory.CreateDirectory(skDir);

IDocumentChunker customChunker = new DocumentChunker(chunkingOptions);
IDocumentChunker skChunker = new SemanticKernelChunker(chunkingOptions);

var excludeSet = new HashSet<string>(excludeDirs, StringComparer.OrdinalIgnoreCase);
var summary = new List<DocComparison>();

foreach (var path in Directory.EnumerateFiles(rootPath, filePattern, SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(rootPath, path);
    if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(seg => excludeSet.Contains(seg)))
        continue;

    var raw = await File.ReadAllTextAsync(path);
    var body = StripFrontmatter(raw);
    if (string.IsNullOrWhiteSpace(body))
        continue;

    var slug = Slugify(Path.GetFileNameWithoutExtension(path));
    const string collection = "compare";

    var customChunks = customChunker.Chunk(collection, slug, body);
    var skChunks = skChunker.Chunk(collection, slug, body);

    await WriteChunksAsync(customDir, slug, customChunks);
    await WriteChunksAsync(skDir, slug, skChunks);

    summary.Add(new DocComparison(
        relative,
        customChunks.Count,
        skChunks.Count,
        customChunks.Count == 0 ? 0 : (int)customChunks.Average(c => c.Content.Length),
        skChunks.Count == 0 ? 0 : (int)skChunks.Average(c => c.Content.Length),
        body.Length));
}

await WriteSummaryAsync(outputDir, summary, chunkingOptions);

Console.WriteLine($"Compared {summary.Count} documents.");
Console.WriteLine($"Output written to: {Path.GetFullPath(outputDir)}");

static string StripFrontmatter(string raw)
{
    if (!raw.StartsWith("---", StringComparison.Ordinal)) return raw;
    var afterOpening = raw.IndexOf('\n', 3);
    if (afterOpening < 0) return raw;

    var searchFrom = afterOpening + 1;
    while (searchFrom < raw.Length)
    {
        var lineEnd = raw.IndexOf('\n', searchFrom);
        var line = lineEnd < 0 ? raw[searchFrom..] : raw[searchFrom..lineEnd];
        if (line.TrimEnd('\r').Trim() == "---")
            return lineEnd < 0 ? string.Empty : raw[(lineEnd + 1)..];
        if (lineEnd < 0) break;
        searchFrom = lineEnd + 1;
    }
    return raw;
}

static string Slugify(string name)
{
    var sb = new StringBuilder(name.Length);
    foreach (var c in name)
    {
        if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
        else if (char.IsWhiteSpace(c) || c == '.') sb.Append('-');
    }
    var slug = sb.ToString().Trim('-');
    return slug.Length > 0 ? slug : "untitled";
}

static async Task WriteChunksAsync(string dir, string slug, IReadOnlyList<Chunk> chunks)
{
    for (int i = 0; i < chunks.Count; i++)
    {
        var file = Path.Combine(dir, $"{slug}-chunk-{i + 1}.txt");
        await File.WriteAllTextAsync(file, chunks[i].Content);
    }
}

static async Task WriteSummaryAsync(string outputDir, List<DocComparison> rows, ChunkingOptions opts)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Chunker comparison summary");
    sb.AppendLine();
    sb.AppendLine($"- TargetChunkSize: {opts.TargetChunkSize}");
    sb.AppendLine($"- ChunkOverlap: {opts.ChunkOverlap}");
    sb.AppendLine($"- Documents: {rows.Count}");
    sb.AppendLine();
    sb.AppendLine("| Document | Body chars | Custom chunks | SK chunks | Custom avg | SK avg |");
    sb.AppendLine("|---|---:|---:|---:|---:|---:|");

    foreach (var r in rows.OrderBy(r => r.Path, StringComparer.Ordinal))
    {
        sb.AppendLine($"| {r.Path} | {r.BodyChars} | {r.CustomChunkCount} | {r.SkChunkCount} | {r.CustomAvgLen} | {r.SkAvgLen} |");
    }

    sb.AppendLine();
    sb.AppendLine("## Aggregate");
    sb.AppendLine();
    sb.AppendLine($"- Total custom chunks: {rows.Sum(r => r.CustomChunkCount)}");
    sb.AppendLine($"- Total SK chunks: {rows.Sum(r => r.SkChunkCount)}");
    sb.AppendLine($"- Docs where custom produced more chunks: {rows.Count(r => r.CustomChunkCount > r.SkChunkCount)}");
    sb.AppendLine($"- Docs where SK produced more chunks: {rows.Count(r => r.SkChunkCount > r.CustomChunkCount)}");
    sb.AppendLine($"- Docs with identical chunk count: {rows.Count(r => r.SkChunkCount == r.CustomChunkCount)}");

    await File.WriteAllTextAsync(Path.Combine(outputDir, "summary.md"), sb.ToString());
}

internal record DocComparison(
    string Path,
    int CustomChunkCount,
    int SkChunkCount,
    int CustomAvgLen,
    int SkAvgLen,
    int BodyChars);
