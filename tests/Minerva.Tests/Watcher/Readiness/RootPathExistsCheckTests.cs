using Microsoft.Extensions.Logging.Abstractions;
using Minerva.MarkdownWatcher;
using Minerva.MarkdownWatcher.Readiness;

namespace Minerva.Tests.Watcher.Readiness;

[Trait("Category", "Readiness")]
public class RootPathExistsCheckTests
{
    [Fact]
    public async Task Passes_WhenDirectoryExistsAndReadable()
    {
        var probe = new FakeProbe(exists: true, throwOnEnumerate: null);
        var check = Build(rootPath: "/some/path", probe);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.CLIENT.ROOT_PATH_OK", result.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Fails_WhenRootPathNullOrWhitespace(string? rootPath)
    {
        var probe = new FakeProbe(exists: false, throwOnEnumerate: null);
        var check = Build(rootPath, probe);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.CLIENT.ROOT_PATH_MISSING", result.Code);
        Assert.Contains("Watcher:RootPath", result.Remediation);
    }

    [Fact]
    public async Task Fails_WhenDirectoryDoesNotExist()
    {
        var probe = new FakeProbe(exists: false, throwOnEnumerate: null);
        var check = Build(rootPath: "/missing/dir", probe);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.CLIENT.ROOT_PATH_MISSING", result.Code);
        Assert.Contains("/missing/dir", result.Remediation);
        Assert.Contains("mkdir -p", result.Remediation);
    }

    [Fact]
    public async Task Fails_WhenDirectoryUnreadable()
    {
        var probe = new FakeProbe(
            exists: true,
            throwOnEnumerate: new UnauthorizedAccessException("permission denied"));
        var check = Build(rootPath: "/sealed", probe);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.CLIENT.ROOT_PATH_UNREADABLE", result.Code);
        Assert.Contains("/sealed", result.Remediation);
    }

    private static RootPathExistsCheck Build(string? rootPath, IRootPathProbe probe) =>
        new(
            new WatcherOptions { RootPath = rootPath!, CollectionName = "x" },
            probe,
            NullLogger<RootPathExistsCheck>.Instance);

    private sealed class FakeProbe : IRootPathProbe
    {
        private readonly bool _exists;
        private readonly Exception? _throwOnEnumerate;

        public FakeProbe(bool exists, Exception? throwOnEnumerate)
        {
            _exists = exists;
            _throwOnEnumerate = throwOnEnumerate;
        }

        public bool DirectoryExists(string path) => _exists;

        public void EnumerateTopLevel(string path)
        {
            if (_throwOnEnumerate is not null) throw _throwOnEnumerate;
        }
    }
}
