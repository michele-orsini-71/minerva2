using Minerva.MarkdownWatcher;
using Minerva.MarkdownWatcher.Readiness;

namespace Minerva.Tests.Watcher.Readiness;

[Trait("Category", "Readiness")]
public class CollectionNameValidCheckTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("Z")]
    [InlineData("0")]
    [InlineData("a-b-c")]
    [InlineData("Abc-123")]
    [InlineData("notes")]
    public async Task Passes_ForValidNames(string name)
    {
        var check = new CollectionNameValidCheck(
            new WatcherOptions { RootPath = "/x", CollectionName = name });

        var result = await check.RunAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("MINERVA.CLIENT.COLLECTION_NAME_OK", result.Code);
    }

    [Theory]
    [InlineData("-leading")]
    [InlineData("with space")]
    [InlineData("with_underscore")]
    [InlineData("Abc!")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Fails_ForInvalidNames(string name)
    {
        var check = new CollectionNameValidCheck(
            new WatcherOptions { RootPath = "/x", CollectionName = name });

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.CLIENT.COLLECTION_NAME_INVALID", result.Code);
        Assert.Contains("^[a-zA-Z0-9][a-zA-Z0-9-]*$", result.Remediation);
    }

    [Fact]
    public async Task Fails_ForNullName()
    {
        var check = new CollectionNameValidCheck(
            new WatcherOptions { RootPath = "/x", CollectionName = null! });

        var result = await check.RunAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MINERVA.CLIENT.COLLECTION_NAME_INVALID", result.Code);
    }
}
