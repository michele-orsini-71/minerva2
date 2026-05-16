using Minerva.Configuration;

namespace Minerva.Tests.Configuration;

[Trait("Category", "Configuration")]
public class MinervaSearchOptionsBinderTests
{
    private const string ValidJson = """
        {
          "ConnectionString": "Host=h;Database=d",
          "Embedding": {
            "BaseUrl": "http://localhost:11434/v1",
            "Model": "nomic",
            "Concurrency": 1,
            "BatchSize": 4
          }
        }
        """;

    [Fact]
    public void Bind_ValidConfig_ProducesFullyFormedRecord()
    {
        var cfg = ConfigFromJson.Build(ValidJson);

        var options = MinervaSearchOptionsBinder.Bind(cfg);

        Assert.Equal("Host=h;Database=d", options.ConnectionString);
        Assert.Equal("http://localhost:11434/v1", options.Embedding.BaseUrl);
        Assert.Equal("nomic", options.Embedding.Model);
        Assert.Equal(1, options.Embedding.Concurrency);
        Assert.Equal(4, options.Embedding.BatchSize);
        Assert.Null(options.Embedding.ApiKey);
    }

    [Fact]
    public void Bind_MissingConnectionString_ThrowsWithSinglePathFailure()
    {
        const string json = """
            {
              "Embedding": {
                "BaseUrl": "http://localhost:11434/v1",
                "Model": "nomic",
                "Concurrency": 1,
                "BatchSize": 4
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Single(ex.Failures);
        Assert.Equal("ConnectionString", ex.Failures[0].Path);
    }

    [Fact]
    public void Bind_InvalidEmbeddingBaseUrl_FailurePathIsDotted()
    {
        const string json = """
            {
              "ConnectionString": "Host=h;Database=d",
              "Embedding": {
                "BaseUrl": "not-a-url",
                "Model": "nomic",
                "Concurrency": 1,
                "BatchSize": 4
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Single(ex.Failures);
        Assert.Equal("Embedding.BaseUrl", ex.Failures[0].Path);
    }

    [Fact]
    public void Bind_MissingEmbeddingSection_ReportsSectionRequired()
    {
        const string json = """
            {
              "ConnectionString": "Host=h;Database=d"
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Contains(ex.Failures, f => f.Path == "Embedding");
    }

    [Fact]
    public void Bind_ConfigWithChunkingExtra_IsIgnored()
    {
        // Search-only hosts should bind a search-shaped config; a Chunking section,
        // if accidentally present, is silently ignored — not an error.
        const string json = """
            {
              "ConnectionString": "Host=h;Database=d",
              "Embedding": {
                "BaseUrl": "http://localhost:11434/v1",
                "Model": "nomic",
                "Concurrency": 1,
                "BatchSize": 4
              },
              "Chunking": {
                "TargetChunkSize": 1200,
                "ChunkOverlap": 200,
                "MaxSegmentChars": 8000,
                "ChunkerType": "Custom"
              }
            }
            """;

        var options = MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json));

        Assert.Equal("Host=h;Database=d", options.ConnectionString);
        Assert.Equal("nomic", options.Embedding.Model);
    }
}
