using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minerva.Readiness;

namespace Minerva.MarkdownWatcher.Readiness;

internal interface IRootPathProbe
{
    bool DirectoryExists(string path);
    void EnumerateTopLevel(string path);
}

public sealed class RootPathExistsCheck : IReadinessCheck, IReadinessCheckTimeout
{
    private readonly WatcherOptions _options;
    private readonly IRootPathProbe _probe;
    private readonly ILogger<RootPathExistsCheck> _logger;

    public RootPathExistsCheck(
        IOptions<WatcherOptions> options,
        ILogger<RootPathExistsCheck> logger)
        : this(options.Value, new FileSystemProbe(), logger) { }

    internal RootPathExistsCheck(
        WatcherOptions options,
        IRootPathProbe probe,
        ILogger<RootPathExistsCheck> logger)
    {
        _options = options;
        _probe = probe;
        _logger = logger;
    }

    public string Name => nameof(RootPathExistsCheck);
    public ReadinessCategory Category => ReadinessCategory.Client;
    public TimeSpan Timeout => TimeSpan.FromSeconds(2);

    public Task<ReadinessCheckResult> RunAsync(CancellationToken ct)
    {
        var path = _options.RootPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(new ReadinessCheckResult(
                Name, Category, Passed: false,
                Code: "MINERVA.CLIENT.ROOT_PATH_MISSING",
                Message: "Watcher:RootPath is not set.",
                Remediation: "Set Watcher:RootPath to the directory you want to index."));
        }

        if (!_probe.DirectoryExists(path))
        {
            return Task.FromResult(new ReadinessCheckResult(
                Name, Category, Passed: false,
                Code: "MINERVA.CLIENT.ROOT_PATH_MISSING",
                Message: $"Watched directory '{path}' does not exist.",
                Remediation: $"Watched directory '{path}' does not exist. Create it (mkdir -p '{path}') or update Watcher:RootPath."));
        }

        try
        {
            _probe.EnumerateTopLevel(path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogDebug(ex, "Root path unreadable");
            return Task.FromResult(new ReadinessCheckResult(
                Name, Category, Passed: false,
                Code: "MINERVA.CLIENT.ROOT_PATH_UNREADABLE",
                Message: $"Process lacks read permission on '{path}'.",
                Remediation: $"Process lacks read permission on '{path}'. Adjust filesystem permissions or run the watcher as a user with access."));
        }

        return Task.FromResult(new ReadinessCheckResult(
            Name, Category, Passed: true,
            Code: "MINERVA.CLIENT.ROOT_PATH_OK",
            Message: null,
            Remediation: null));
    }

    private sealed class FileSystemProbe : IRootPathProbe
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public void EnumerateTopLevel(string path)
        {
            using var enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            enumerator.MoveNext();
        }
    }
}
