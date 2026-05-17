using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Minerva.Models;

namespace Minerva.MarkdownIndexer;

public sealed class MarkdownIndexer
{
    private readonly MarkdownScanner _scanner;
    private readonly IIngestEngine _engine;
    private readonly string _collectionName;
    private readonly bool _allowRecreateOnEmbedderMismatch;
    private readonly ILogger<MarkdownIndexer> _logger;

    public MarkdownIndexer(
        MarkdownScanner scanner,
        IIngestEngine engine,
        string collectionName,
        bool allowRecreateOnEmbedderMismatch,
        ILogger<MarkdownIndexer> logger)
    {
        _scanner = scanner;
        _engine = engine;
        _collectionName = collectionName;
        _allowRecreateOnEmbedderMismatch = allowRecreateOnEmbedderMismatch;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var result = await _engine.IngestAsync(
            _collectionName, EnumerateDocumentsAsync(ct), _allowRecreateOnEmbedderMismatch, ct);

        _logger.LogInformation(
            "Sync: +{Added} ~{Updated} -{Deleted} ={Unchanged} in {Elapsed}",
            result.Added, result.Updated, result.Deleted, result.Unchanged, result.Elapsed);
    }

    private async IAsyncEnumerable<Document> EnumerateDocumentsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var path in _scanner.ScanFiles())
        {
            ct.ThrowIfCancellationRequested();
            Document doc;
            try
            {
                doc = _scanner.ReadFile(path);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Skipping unreadable file {Path}", path);
                continue;
            }
            yield return doc;
            await Task.Yield();
        }
    }
}
