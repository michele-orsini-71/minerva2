using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Ingestion;

namespace Minerva.MarkdownWatcher;

public class MarkdownSyncService : BackgroundService
{
    private readonly IMinervaEngine _engine;
    private readonly IMarkdownScanner _scanner;
    private readonly MarkdownIngestionHandler _handler;
    private readonly IEmbeddingDimensionProvider _dimensionProvider;
    private readonly WatcherOptions _watcherOptions;
    private readonly MinervaOptions _minervaOptions;
    private readonly ILogger<MarkdownSyncService> _logger;

    private PathDebouncer? _debouncer;
    private FileSystemWatcher? _fsWatcher;

    public MarkdownSyncService(
        IMinervaEngine engine,
        IMarkdownScanner scanner,
        MarkdownIngestionHandler handler,
        IEmbeddingDimensionProvider dimensionProvider,
        IOptions<WatcherOptions> watcherOptions,
        IOptions<MinervaOptions> minervaOptions,
        ILogger<MarkdownSyncService> logger)
    {
        _engine = engine;
        _scanner = scanner;
        _handler = handler;
        _dimensionProvider = dimensionProvider;
        _watcherOptions = watcherOptions.Value;
        _minervaOptions = minervaOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_watcherOptions.RootPath))
            throw new ConfigurationException("WatcherOptions.RootPath must be configured.");
        if (string.IsNullOrWhiteSpace(_watcherOptions.CollectionName))
            throw new ConfigurationException("WatcherOptions.CollectionName must be configured.");
        if (!Directory.Exists(_watcherOptions.RootPath))
            throw new ConfigurationException(
                $"WatcherOptions.RootPath '{_watcherOptions.RootPath}' does not exist.");

        await EnsureCollectionAsync(stoppingToken).ConfigureAwait(false);

        _debouncer = new PathDebouncer(
            TimeSpan.FromMilliseconds(_watcherOptions.DebounceMs),
            _handler.OnChangedAsync);

        await InitialScanAsync(stoppingToken).ConfigureAwait(false);

        StartFileSystemWatcher();

        _logger.LogInformation(
            "Watching {RootPath} for {Pattern} changes (collection: {Collection})",
            _watcherOptions.RootPath, _watcherOptions.FilePattern, _watcherOptions.CollectionName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _fsWatcher?.Dispose();
        _debouncer?.Dispose();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        var existing = await _engine.Collections.GetAsync(_watcherOptions.CollectionName, ct);
        if (existing is not null)
            return;

        var dimension = await _dimensionProvider.GetDimensionAsync(ct);
        _logger.LogInformation(
            "Creating collection '{Collection}' with model '{Model}' ({Dim} dims)",
            _watcherOptions.CollectionName, _minervaOptions.Embedding.Model, dimension);

        await _engine.Collections.CreateAsync(
            _watcherOptions.CollectionName,
            _minervaOptions.Embedding.Model,
            dimension,
            description: $"Auto-created by Minerva.MarkdownWatcher for {_watcherOptions.RootPath}",
            ct: ct);
    }

    private async Task InitialScanAsync(CancellationToken ct)
    {
        var files = _scanner.ScanFiles();
        _logger.LogInformation("Initial scan: {Count} files", files.Count);

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) return;
            await _handler.OnChangedAsync(file, ct).ConfigureAwait(false);
        }
    }

    private void StartFileSystemWatcher()
    {
        _fsWatcher = new FileSystemWatcher(_watcherOptions.RootPath, _watcherOptions.FilePattern)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        _fsWatcher.Created += (_, e) => _debouncer?.Schedule(e.FullPath);
        _fsWatcher.Changed += (_, e) => _debouncer?.Schedule(e.FullPath);
        _fsWatcher.Deleted += (_, e) => _ = _handler.OnDeletedAsync(e.FullPath);
        _fsWatcher.Renamed += (_, e) =>
        {
            _ = _handler.OnDeletedAsync(e.OldFullPath);
            _debouncer?.Schedule(e.FullPath);
        };
        _fsWatcher.Error += (_, e) =>
            _logger.LogError(e.GetException(), "FileSystemWatcher error");
    }
}
