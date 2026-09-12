using System.Collections;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Minerva.Exceptions;
using Minerva.Models;

namespace Minerva.MarkdownIndexer;

public sealed class MarkdownIndexer
{
    public static readonly string ClientProvenanceName = "minerva-markdown-indexer";
    public static readonly string RootPathField = "root-path";
    public static readonly string ExcludeDirectoriesField = "exclude-directories";
    public static readonly string FileExtensionsField = "file-extensions";

    private readonly MarkdownScanner _scanner;
    private readonly IIngestEngine _engine;
    private readonly IndexerOptions _options;
    private readonly ILogger<MarkdownIndexer> _logger;

    public MarkdownIndexer(
        MarkdownScanner scanner,
        IIngestEngine engine,
        IndexerOptions options,
        ILogger<MarkdownIndexer> logger)
    {
        _scanner = scanner;
        _engine = engine;
        _options = options;
        _logger = logger;
    }

    public async Task<IngestionResult> RunAsync(CancellationToken ct = default)
    {
        var collection = await _engine.QueryCollectionInfoAsync(_options.CollectionName, ct);
        if (collection != null)
        {
            if (collection.ClientProvenance.kind != ClientProvenanceName)
            {
                throw new NotAnIndexerCollectionException(collection.ClientProvenance.kind);
            }

            EnsureScopeUnchanged(collection.ClientProvenance);
        }

        var clientData = new Dictionary<string, object>
        {
            [RootPathField] = _options.RootPath,
            [ExcludeDirectoriesField] = _options.ExcludeDirectories,
            [FileExtensionsField] = _options.FileExtensions,
        };

        var clientProvenance = new ClientProvenance(ClientProvenanceName, clientData);

        try
        {
            var result = await _engine.IngestAsync(
                _options.CollectionName, clientProvenance, EnumerateDocumentsAsync(ct), _options.AllowRecreateOnConfigMismatch, ct);

            _logger.LogInformation(
                "Sync: +{Added} ~{Updated} -{Deleted} ={Unchanged} !!{Failed} in {Elapsed}",
                result.Added, result.Updated, result.Deleted, result.Unchanged, result.Failed, result.Elapsed);
            return result;
        }
        catch (IngestionAbortedException ex)
        {
            // Logged here so the file log records the partial outcome; the caller maps it to an exit code.
            _logger.LogError(ex, "{Message}, partial result: +{Ingested} !!{Failed} in {Elapsed}",
                ex.Message, ex.Ingested, ex.Failed, ex.Elapsed);
            throw;
        }
    }

    // A scope field defines which files belong to the corpus. A change to any of them can
    // silently add or remove records, exactly like a root-path change, so each is compared with
    // the same semantics the scanner uses for selection. Root path is compared exactly; the lists
    // are compared as sets, because reordering or duplication does not change the selected files.
    private void EnsureScopeUnchanged(ClientProvenance stored)
    {
        var changed = new List<string>();

        if (ReadProvenanceString(stored, RootPathField) != _options.RootPath)
        {
            changed.Add(RootPathField);
        }

        if (!SetEquals(
                ReadProvenanceStringList(stored, ExcludeDirectoriesField),
                _options.ExcludeDirectories, s => s, StringComparer.OrdinalIgnoreCase))
        {
            changed.Add(ExcludeDirectoriesField);
        }

        if (!SetEquals(
                ReadProvenanceStringList(stored, FileExtensionsField),
                _options.FileExtensions, MarkdownScanner.NormalizeExtension, StringComparer.Ordinal))
        {
            changed.Add(FileExtensionsField);
        }

        if (changed.Count > 0 && !_options.AllowSourceScopeChange)
        {
            throw new CollectionScopeChangeNotAllowedException(changed);
        }
    }

    private static string ReadProvenanceString(ClientProvenance provenance, string field)
    {
        if (provenance.data.TryGetValue(field, out var value) && value is string s)
        {
            return s;
        }
        throw new ProvenanceMalformedException();
    }

    private static IReadOnlyList<string> ReadProvenanceStringList(ClientProvenance provenance, string field)
    {
        if (provenance.data.TryGetValue(field, out var value) && value is IEnumerable items and not string)
        {
            var list = new List<string>();
            foreach (var item in items)
            {
                if (item is not string s)
                {
                    throw new ProvenanceMalformedException();
                }
                list.Add(s);
            }
            return list;
        }
        throw new ProvenanceMalformedException();
    }

    private static bool SetEquals(
        IReadOnlyList<string> stored,
        IReadOnlyList<string> current,
        Func<string, string> normalize,
        StringComparer comparer)
    {
        var a = new HashSet<string>(stored.Select(normalize), comparer);
        var b = new HashSet<string>(current.Select(normalize), comparer);
        return a.SetEquals(b);
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
