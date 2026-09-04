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
            "MaxSegmentChars": 8000,
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
        Assert.Equal(8000, options.Chunking.MaxSegmentChars);
        Assert.Equal(ChunkerType.Custom, options.Chunking.ChunkerType);
        Assert.Null(options.Chunking.Llm);
    }

    [Fact]
    public void Bind_ChunkingLlmAbsent_ProducesNullLlm()
    {
        var cfg = ConfigFromJson.Build(ValidJson);

        var options = MinervaIngestOptionsBinder.Bind(cfg);

        Assert.Null(options.Chunking.Llm);
    }

    [Fact]
    public void Bind_ChunkingLlmPresent_ProducesPopulatedLlm()
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
                "MaxSegmentChars": 8000,
                "ChunkerType": "Custom",
                "Llm": {
                  "BaseUrl": "http://localhost:1234/v1",
                  "Model": "gemma",
                  "Concurrency": 2
                }
              }
            }
            """;

        var options = MinervaIngestOptionsBinder.Bind(ConfigFromJson.Build(json));

        Assert.NotNull(options.Chunking.Llm);
        Assert.Equal("http://localhost:1234/v1", options.Chunking.Llm!.BaseUrl);
        Assert.Equal("gemma", options.Chunking.Llm.Model);
        Assert.Equal(2, options.Chunking.Llm.Concurrency);
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
                "MaxSegmentChars": 8000,
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
                "MaxSegmentChars": 8000,
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
    public void Bind_LlmGrandchildFailure_PathIsDeeplyDotted()
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
                "MaxSegmentChars": 8000,
                "ChunkerType": "Custom",
                "Llm": {
                  "BaseUrl": "http://localhost:1234/v1",
                  "Concurrency": 1
                }
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaIngestOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Single(ex.Failures);
        Assert.Equal("Chunking.Llm.Model", ex.Failures[0].Path);
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
                "MaxSegmentChars": 8000,
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
    public void Bind_NestedFailures_ParentChildAndGrandchild_AllAppear()
    {
        const string json = """
            {
              "Embedding": {
                "BaseUrl": "http://localhost:11434/v1",
                "Concurrency": 1,
                "BatchSize": 4
              },
              "Chunking": {
                "TargetChunkSize": 1200,
                "ChunkOverlap": 200,
                "MaxSegmentChars": 8000,
                "ChunkerType": "Custom",
                "Llm": {
                  "BaseUrl": "bad-url",
                  "Concurrency": 1
                }
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaIngestOptionsBinder.Bind(ConfigFromJson.Build(json)));

        var paths = ex.Failures.Select(f => f.Path).ToList();
        Assert.Contains("ConnectionString", paths);              // top-level
        Assert.Contains("Embedding.Model", paths);                // child
        Assert.Contains("Chunking.Llm.BaseUrl", paths);           // grandchild
        Assert.Contains("Chunking.Llm.Model", paths);             // grandchild
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
                "MaxSegmentChars": 8000,
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
                "MaxSegmentChars": 8000,
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
