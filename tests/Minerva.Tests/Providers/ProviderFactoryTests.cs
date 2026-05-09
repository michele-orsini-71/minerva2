using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Providers;
using Minerva.Tests.TestSupport;

namespace Minerva.Tests.Providers;

[Trait("Category", "Providers")]
[Collection("EnvVars")]
public class ProviderFactoryTests
{
    private static EmbeddingProviderOptions ValidEmbedding(string? apiKey = null) =>
        TestOptions.Embedding(
            baseUrl: "http://localhost:11434",
            model: "test-model",
            apiKey: apiKey);

    private static LlmProviderOptions ValidLlm(string? apiKey = null) =>
        TestOptions.Llm(
            baseUrl: "http://localhost:11434",
            model: "test-model",
            apiKey: apiKey);

    [Fact]
    public void CreateEmbeddingProvider_ValidOptions_ReturnsProvider()
    {
        var factory = new ProviderFactory(ValidEmbedding(), llm: null);
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
                ValidEmbedding(apiKey: "${MINERVA_TEST_KEY}"), llm: null);
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
            () => new ProviderFactory(ValidEmbedding(apiKey: "sk-12345"), llm: null));
    }

    [Fact]
    public void CreateLlmProvider_ValidOptions_ReturnsProvider()
    {
        var factory = new ProviderFactory(ValidEmbedding(), llm: ValidLlm());

        Assert.True(factory.HasLlm);
        using var provider = (OpenAICompatibleLlmProvider)factory.CreateLlmProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void HasLlm_NullLlmOptions_IsFalse()
    {
        var factory = new ProviderFactory(ValidEmbedding(), llm: null);

        Assert.False(factory.HasLlm);
    }

    [Fact]
    public void CreateLlmProvider_LiteralApiKey_ThrowsConfigurationException()
    {
        Assert.Throws<ConfigurationException>(
            () => new ProviderFactory(ValidEmbedding(), llm: ValidLlm(apiKey: "AIzaSyTest123")));
    }
}
