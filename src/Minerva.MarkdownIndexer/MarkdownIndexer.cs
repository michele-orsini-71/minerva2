using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Minerva.Models;

namespace Minerva.MarkdownIndexer;

public sealed class MarkdownIndexer
{
    public static readonly string ClientProvenanceName = "markdown-indexer";
    public static readonly string RootPathField = "root-path";

    private readonly MarkdownScanner _scanner;
    private readonly IIngestEngine _engine;
    private readonly ISearchEngine _searchEngine;
    private readonly IndexerOptions _options;
    private readonly ILogger<MarkdownIndexer> _logger;

    public MarkdownIndexer(
        MarkdownScanner scanner,
        IIngestEngine engine,
        ISearchEngine searchEngine,
        IndexerOptions options,
        ILogger<MarkdownIndexer> logger)
    {
        _scanner = scanner;
        _engine = engine;
        _searchEngine = searchEngine;
        _options = options;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var collection = await _searchEngine.QueryCollectionInfoAsync(_options.CollectionName, ct);
        if (collection != null)
        {
            if (collection.ClientProvenance.kind != ClientProvenanceName)
            {
                throw new NotAnIndexerCollectionException(collection.ClientProvenance.kind);
            }

            object? rawPath;
            if (collection.ClientProvenance.data.TryGetValue(RootPathField, out rawPath) && rawPath is string collectionRootPath)
            {
                if (collectionRootPath != _options.RootPath && !_options.AllowSourceRootChange)
                {
                    throw new CollectionPathChangeNotAllowedException(collectionRootPath);
                }
            } else
            {
                throw new ProvenanceMalformedExeption();  
            }
        }

        var clientData = new Dictionary<string, object>
        {
            [RootPathField] = _options.RootPath
        };

        var clientProvenance = new ClientProvenance(ClientProvenanceName, clientData);

        var result = await _engine.IngestAsync(
            _options.CollectionName, clientProvenance, EnumerateDocumentsAsync(ct), _options.AllowRecreateOnConfigMismatch, ct);

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
