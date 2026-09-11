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

    [Fact]
    public void CreateEmbeddingProvider_ValidOptions_ReturnsProvider()
    {
        var factory = new ProviderFactory(ValidEmbedding());
        using var provider = (OpenAICompatibleEmbeddingProvider)factory.CreateEmbeddingProvider();

        Assert.NotNull(provider);
    }

    [Fact]
    public void CreateEmbeddingProvider_WithEnvVarApiKey_ReturnsProvider()
    {
        Environment.SetEnvironmentVariable("MINERVA_TEST_KEY", "resolved-value");
        try
        {
            var factory = new ProviderFactory(ValidEmbedding(apiKey: "${MINERVA_TEST_KEY}"));
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
            () => new ProviderFactory(ValidEmbedding(apiKey: "sk-12345")));
    }
}
