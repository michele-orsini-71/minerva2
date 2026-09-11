using Minerva.Configuration;

namespace Minerva.Tests.Configuration;

[Trait("Category", "Configuration")]
public class MinervaSearchOptionsBinderTests
{
    private const string ValidJson = """
        {
          "Minerva": {
            "ConnectionString": "Host=h;Database=d",
            "Embedding": {
              "BaseUrl": "http://localhost:11434/v1",
              "Model": "nomic",
              "Concurrency": 1,
              "BatchSize": 4
            }
          },
          "Search": {
            "TopK": 10,
            "HybridAlpha": 0.5,
            "CandidatePoolSize": 50,
            "ExpandContext": false,
            "EnableReranker": false
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
        Assert.Equal(10, options.TopK);
        Assert.Equal(0.5, options.HybridAlpha);
        Assert.Equal(50, options.CandidatePoolSize);
        Assert.False(options.ExpandContext);
        Assert.False(options.EnableReranker);
        Assert.Null(options.Reranker);
    }

    [Fact]
    public void Bind_MissingConnectionString_ReportsMinervaConnectionStringFailure()
    {
        const string json = """
            {
              "Minerva": {
                "Embedding": {
                  "BaseUrl": "http://localhost:11434/v1",
                  "Model": "nomic",
                  "Concurrency": 1,
                  "BatchSize": 4
                }
              },
              "Search": {
                "TopK": 10,
                "HybridAlpha": 0.5,
                "CandidatePoolSize": 50,
                "ExpandContext": false
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Contains(ex.Failures, f => f.Path == "Minerva.ConnectionString");
    }

    [Fact]
    public void Bind_InvalidEmbeddingBaseUrl_FailurePathIsDotted()
    {
        const string json = """
            {
              "Minerva": {
                "ConnectionString": "Host=h;Database=d",
                "Embedding": {
                  "BaseUrl": "not-a-url",
                  "Model": "nomic",
                  "Concurrency": 1,
                  "BatchSize": 4
                }
              },
              "Search": {
                "TopK": 10,
                "HybridAlpha": 0.5,
                "CandidatePoolSize": 50,
                "ExpandContext": false
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Contains(ex.Failures, f => f.Path == "Minerva.Embedding.BaseUrl");
    }

    [Fact]
    public void Bind_MissingEmbeddingSection_ReportsSectionRequired()
    {
        const string json = """
            {
              "Minerva": {
                "ConnectionString": "Host=h;Database=d"
              },
              "Search": {
                "TopK": 10,
                "HybridAlpha": 0.5,
                "CandidatePoolSize": 50,
                "ExpandContext": false
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Contains(ex.Failures, f => f.Path == "Minerva.Embedding");
    }

    [Fact]
    public void Bind_MissingSearchSection_ReportsSectionRequired()
    {
        const string json = """
            {
              "Minerva": {
                "ConnectionString": "Host=h;Database=d",
                "Embedding": {
                  "BaseUrl": "http://localhost:11434/v1",
                  "Model": "nomic",
                  "Concurrency": 1,
                  "BatchSize": 4
                }
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Contains(ex.Failures, f => f.Path == "Search");
    }

    [Fact]
    public void Bind_MissingSearchTopK_ReportsRequired()
    {
        const string json = """
            {
              "Minerva": {
                "ConnectionString": "Host=h;Database=d",
                "Embedding": {
                  "BaseUrl": "http://localhost:11434/v1",
                  "Model": "nomic",
                  "Concurrency": 1,
                  "BatchSize": 4
                }
              },
              "Search": {
                "HybridAlpha": 0.5,
                "CandidatePoolSize": 50,
                "ExpandContext": false
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Contains(ex.Failures, f => f.Path == "Search.TopK");
    }

    [Fact]
    public void Bind_NonPositiveTopK_ReportsRangeFailure()
    {
        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(SearchJsonWith(topK: 0))));

        Assert.Contains(ex.Failures, f => f.Path == "Search.TopK");
    }

    [Fact]
    public void Bind_HybridAlphaOutOfRange_ReportsRangeFailure()
    {
        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(SearchJsonWith(hybridAlpha: 1.5))));

        Assert.Contains(ex.Failures, f => f.Path == "Search.HybridAlpha");
    }

    [Fact]
    public void Bind_NonPositiveCandidatePoolSize_ReportsRangeFailure()
    {
        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(SearchJsonWith(candidatePoolSize: 0))));

        Assert.Contains(ex.Failures, f => f.Path == "Search.CandidatePoolSize");
    }

    [Fact]
    public void Bind_CandidatePoolSizeLessThanTopK_ReportsRangeFailure()
    {
        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(
                ConfigFromJson.Build(SearchJsonWith(topK: 10, candidatePoolSize: 5))));

        Assert.Contains(ex.Failures, f => f.Path == "Search.CandidatePoolSize");
    }

    [Fact]
    public void Bind_MissingExpandContext_ReportsRequired()
    {
        const string json = """
            {
              "Minerva": {
                "ConnectionString": "Host=h;Database=d",
                "Embedding": {
                  "BaseUrl": "http://localhost:11434/v1",
                  "Model": "nomic",
                  "Concurrency": 1,
                  "BatchSize": 4
                }
              },
              "Search": {
                "TopK": 10,
                "HybridAlpha": 0.5,
                "CandidatePoolSize": 50
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Contains(ex.Failures, f => f.Path == "Search.ExpandContext");
    }

    [Fact]
    public void Bind_MissingEnableReranker_ReportsRequired()
    {
        const string json = """
            {
              "Minerva": {
                "ConnectionString": "Host=h;Database=d",
                "Embedding": {
                  "BaseUrl": "http://localhost:11434/v1",
                  "Model": "nomic",
                  "Concurrency": 1,
                  "BatchSize": 4
                }
              },
              "Search": {
                "TopK": 10,
                "HybridAlpha": 0.5,
                "CandidatePoolSize": 50,
                "ExpandContext": false
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Contains(ex.Failures, f => f.Path == "Search.EnableReranker");
    }

    [Fact]
    public void Bind_EnableRerankerTrueButNoRerankerSection_ReportsFailure()
    {
        const string json = """
            {
              "Minerva": {
                "ConnectionString": "Host=h;Database=d",
                "Embedding": {
                  "BaseUrl": "http://localhost:11434/v1",
                  "Model": "nomic",
                  "Concurrency": 1,
                  "BatchSize": 4
                }
              },
              "Search": {
                "TopK": 10,
                "HybridAlpha": 0.5,
                "CandidatePoolSize": 50,
                "ExpandContext": false,
                "EnableReranker": true
              }
            }
            """;

        var ex = Assert.Throws<OptionsValidationException>(
            () => MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json)));

        Assert.Contains(ex.Failures, f => f.Path == "Search.EnableReranker");
    }

    [Fact]
    public void Bind_EnableRerankerTrueWithRerankerSection_BindsReranker()
    {
        const string json = """
            {
              "Minerva": {
                "ConnectionString": "Host=h;Database=d",
                "Embedding": {
                  "BaseUrl": "http://localhost:11434/v1",
                  "Model": "nomic",
                  "Concurrency": 1,
                  "BatchSize": 4
                },
                "Reranker": {
                  "BaseUrl": "http://localhost:9932",
                  "Model": "bge-reranker"
                }
              },
              "Search": {
                "TopK": 10,
                "HybridAlpha": 0.5,
                "CandidatePoolSize": 50,
                "ExpandContext": false,
                "EnableReranker": true
              }
            }
            """;

        var options = MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json));

        Assert.True(options.EnableReranker);
        Assert.NotNull(options.Reranker);
        Assert.Equal("http://localhost:9932", options.Reranker!.BaseUrl);
        Assert.Equal("bge-reranker", options.Reranker.Model);
    }

    [Fact]
    public void Bind_ConfigWithChunkingExtra_IsIgnored()
    {
        // Search-only hosts should bind a search-shaped config; a Chunking section,
        // if accidentally present, is silently ignored — not an error.
        const string json = """
            {
              "Minerva": {
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
              },
              "Search": {
                "TopK": 10,
                "HybridAlpha": 0.5,
                "CandidatePoolSize": 50,
                "ExpandContext": false,
                "EnableReranker": false
              }
            }
            """;

        var options = MinervaSearchOptionsBinder.Bind(ConfigFromJson.Build(json));

        Assert.Equal("Host=h;Database=d", options.ConnectionString);
        Assert.Equal("nomic", options.Embedding.Model);
    }

    private static string SearchJsonWith(
        int topK = 10,
        double hybridAlpha = 0.5,
        int candidatePoolSize = 50,
        bool expandContext = false)
    {
        var alpha = hybridAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ec = expandContext ? "true" : "false";
        return $$"""
            {
              "Minerva": {
                "ConnectionString": "Host=h;Database=d",
                "Embedding": {
                  "BaseUrl": "http://localhost:11434/v1",
                  "Model": "nomic",
                  "Concurrency": 1,
                  "BatchSize": 4
                }
              },
              "Search": {
                "TopK": {{topK}},
                "HybridAlpha": {{alpha}},
                "CandidatePoolSize": {{candidatePoolSize}},
                "ExpandContext": {{ec}},
                "EnableReranker": false
              }
            }
            """;
    }
}
