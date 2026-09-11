using Minerva.Configuration;
using Minerva.Models;

namespace Minerva.Tests.Configuration;

[Trait("Category", "Configuration")]
public class MinervaIngestOptionsBinderTests
{
    private const string ValidJson = """
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
            "ChunkerType": "Custom"
          }
        }
        """;

    [Fact]
    public void Bind_ValidConfig_ProducesFullyFormedRecord()
    {
        var cfg = ConfigFromJson.Build(ValidJson);

        var options = MinervaIngestOptionsBinder.Bind(cfg);

        Assert.Equal("Host=h;Database=d", options.ConnectionString);
        Assert.Equal("http://localhost:11434/v1", options.Embedding.BaseUrl);
        Assert.Equal("nomic", options.Embedding.Model);
        Assert.Equal(1, options.Embedding.Concurrency);
        Assert.Equal(4, options.Embedding.BatchSize);
        Assert.Null(options.Embedding.ApiKey);
        Assert.Equal(1200, options.Chunking.TargetChunkSize);
        Assert.Equal(200, options.Chunking.ChunkOverlap);
        Assert.Equal(ChunkerType.Custom, options.Chunking.ChunkerType);
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
              },
              "Chunking": {
                "TargetChunkSize": 1200,
                "ChunkOverlap": 200,
                "ChunkerType": "Custom"
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaIngestOptionsBinder.Bind(ConfigFromJson.Build(json)));

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
              },
              "Chunking": {
                "TargetChunkSize": 1200,
                "ChunkOverlap": 200,
                "ChunkerType": "Custom"
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaIngestOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Single(ex.Failures);
        Assert.Equal("Embedding.BaseUrl", ex.Failures[0].Path);
    }

    [Fact]
    public void Bind_MultipleSiblingFailures_AreAllCollected()
    {
        const string json = """
            {
              "Embedding": {
                "Model": "nomic",
                "Concurrency": 1,
                "BatchSize": 4
              },
              "Chunking": {
                "ChunkOverlap": 200,
                "ChunkerType": "Custom"
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaIngestOptionsBinder.Bind(ConfigFromJson.Build(json)));

        var paths = ex.Failures.Select(f => f.Path).ToList();
        Assert.Contains("ConnectionString", paths);
        Assert.Contains("Embedding.BaseUrl", paths);
        Assert.Contains("Chunking.TargetChunkSize", paths);
    }

    [Fact]
    public void Bind_InvalidChunkerType_ReportsEnumPath()
    {
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
                "ChunkerType": "BogusValue"
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaIngestOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Single(ex.Failures);
        Assert.Equal("Chunking.ChunkerType", ex.Failures[0].Path);
    }

    [Fact]
    public void Bind_OverlapNotLessThanTarget_ReportsOverlapPath()
    {
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
                "TargetChunkSize": 100,
                "ChunkOverlap": 200,
                "ChunkerType": "Custom"
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaIngestOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Contains(ex.Failures, f => f.Path == "Chunking.ChunkOverlap");
    }

    [Fact]
    public void Bind_MissingChunkingSection_ReportsSectionRequired()
    {
        const string json = """
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

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaIngestOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Contains(ex.Failures, f => f.Path == "Chunking");
    }
}
