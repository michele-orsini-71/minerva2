using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Providers;

namespace Minerva.Tests.Providers;

[Trait("Category", "Providers")]
[Collection("EnvVars")]
public class ProviderFactoryTests
{
    private static ProviderOptions ValidOptions(string? apiKey = null) => new()
    {
        BaseUrl = "http://localhost:11434",
        Model = "test-model",
        ApiKey = apiKey,
        Concurrency = 1,
        RequestsPerMinute = null,
    };

    [Fact]
    public void CreateEmbeddingProvider_ValidOptions_ReturnsProvider()
    {
        var factory = new ProviderFactory(ValidOptions(), llm: null);
        using var provider = (OpenAICompatibleEmbeddingProvider)factory.CreateEmbeddingProvider();

        Assert.NotNull(provider);
    }

    [Fact]
    public void CreateEmbeddingProvider_WithEnvVarApiKey_ReturnsProvider()
    {
        Environment.SetEnvironmentVariable("MINERVA_TEST_KEY", "resolved-value");
        try
        {
            var factory = new ProviderFactory(
                ValidOptions(apiKey: "${MINERVA_TEST_KEY}"), llm: null);
            using var provider = (OpenAICompatibleEmbeddingProvider)factory.CreateEmbeddingProvider();

            Assert.NotNull(provider);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MINERVA_TEST_KEY", null);
        }
    }

    [Fact]
    public void CreateEmbeddingProvider_LiteralApiKey_ThrowsConfigurationException()
    {
        // Resolution happens eagerly in the factory ctor.
        Assert.Throws<ConfigurationException>(
            () => new ProviderFactory(ValidOptions(apiKey: "sk-12345"), llm: null));
    }

    [Fact]
    public void CreateLlmProvider_ValidOptions_ReturnsProvider()
    {
        var factory = new ProviderFactory(ValidOptions(), llm: ValidOptions());

        Assert.True(factory.HasLlm);
        using var provider = (OpenAICompatibleLlmProvider)factory.CreateLlmProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void HasLlm_NullLlmOptions_IsFalse()
    {
        var factory = new ProviderFactory(ValidOptions(), llm: null);

        Assert.False(factory.HasLlm);
    }

    [Fact]
    public void CreateLlmProvider_LiteralApiKey_ThrowsConfigurationException()
    {
        Assert.Throws<ConfigurationException>(
            () => new ProviderFactory(ValidOptions(), llm: ValidOptions(apiKey: "AIzaSyTest123")));
    }
}
