using Minerva.Configuration;
using Minerva.MarkdownIndexer;

namespace Minerva.Tests.Configuration;

[Trait("Category", "Configuration")]
public class IndexerOptionsBinderTests
{
    [Fact]
    public void Bind_ValidConfig_ProducesFullyFormedRecord()
    {
        const string json = """
            {
              "RootPath": "/some/path",
              "CollectionName": "my-notes",
              "ExcludeDirectories": [".git", ".obsidian"],
              "AllowRecreateOnEmbedderMismatch": true
            }
            """;

        var options = IndexerOptionsBinder.Bind(ConfigFromJson.Build(json));

        Assert.Equal("/some/path", options.RootPath);
        Assert.Equal("my-notes", options.CollectionName);
        Assert.Equal(2, options.ExcludeDirectories.Count);
        Assert.Contains(".git", options.ExcludeDirectories);
        Assert.Contains(".obsidian", options.ExcludeDirectories);
        Assert.True(options.AllowRecreateOnEmbedderMismatch);
    }

    [Fact]
    public void Bind_MissingFields_ReportsAll()
    {
        const string json = "{}";

        var ex = Assert.Throws<OptionsValidationException>(
            () => IndexerOptionsBinder.Bind(ConfigFromJson.Build(json)));

        var paths = ex.Failures.Select(f => f.Path).ToList();
        Assert.Contains("RootPath", paths);
        Assert.Contains("CollectionName", paths);
        Assert.Contains("ExcludeDirectories", paths);
        Assert.Contains("AllowRecreateOnEmbedderMismatch", paths);
    }

    [Fact]
    public void Bind_InvalidCollectionName_FailureMentionsRegex()
    {
        const string json = """
            {
              "RootPath": "/x",
              "CollectionName": "bad name!",
              "ExcludeDirectories": [],
              "AllowRecreateOnEmbedderMismatch": false
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => IndexerOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Single(ex.Failures);
        Assert.Equal("CollectionName", ex.Failures[0].Path);
    }

    [Fact]
    public void Bind_EmptyExcludeDirectories_AllowedAsExplicitEmpty()
    {
        const string json = """
            {
              "RootPath": "/x",
              "CollectionName": "ok",
              "ExcludeDirectories": [],
              "AllowRecreateOnEmbedderMismatch": false
            }
            """;

        var options = IndexerOptionsBinder.Bind(ConfigFromJson.Build(json));

        Assert.Empty(options.ExcludeDirectories);
    }
}
