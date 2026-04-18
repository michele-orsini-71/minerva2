using Microsoft.Extensions.Logging;

namespace Minerva.MarkdownWatcher;

public class MarkdownIngestionHandler
{
    private readonly IMinervaEngine _engine;
    private readonly IMarkdownScanner _scanner;
    private readonly string _collectionName;
    private readonly ILogger<MarkdownIngestionHandler> _logger;

    public MarkdownIngestionHandler(
        IMinervaEngine engine,
        IMarkdownScanner scanner,
        string collectionName,
        ILogger<MarkdownIngestionHandler> logger)
    {
        _engine = engine;
        _scanner = scanner;
        _collectionName = collectionName;
        _logger = logger;
    }

    public async Task OnChangedAsync(string fullPath, CancellationToken ct = default)
    {
        if (!File.Exists(fullPath))
        {
            await OnDeletedAsync(fullPath).ConfigureAwait(false);
            return;
        }

        if (_scanner.IsExcluded(fullPath)) return;

        try
        {
            var doc = _scanner.ReadFile(fullPath);
            var result = await _engine.IngestAsync(_collectionName, doc, ct)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "Ingested {Source}: +{Added} ~{Updated} -{Deleted} ={Unchanged}",
                doc.SourceId, result.Added, result.Updated, result.Deleted, result.Unchanged);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to ingest {Path}", fullPath);
        }
    }

    public async Task OnDeletedAsync(string fullPath)
    {
        try
        {
            var sourceId = _scanner.DeriveSourceId(fullPath);
            await _engine.RemoveAsync(_collectionName, sourceId).ConfigureAwait(false);
            _logger.LogInformation("Removed {Source}", sourceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove {Path}", fullPath);
        }
    }
}
