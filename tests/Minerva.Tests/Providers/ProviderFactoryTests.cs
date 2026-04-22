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
        var factory = new ProviderFactory();
        using var provider = factory.CreateEmbeddingProvider(ValidOptions());

        Assert.NotNull(provider);
        Assert.IsType<OpenAICompatibleEmbeddingProvider>(provider);
    }

    [Fact]
    public void CreateEmbeddingProvider_WithEnvVarApiKey_ReturnsProvider()
    {
        Environment.SetEnvironmentVariable("MINERVA_TEST_KEY", "resolved-value");
        try
        {
            var factory = new ProviderFactory();
            using var provider = factory.CreateEmbeddingProvider(
                ValidOptions(apiKey: "${MINERVA_TEST_KEY}"));

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
        var factory = new ProviderFactory();

        Assert.Throws<ConfigurationException>(
            () => factory.CreateEmbeddingProvider(ValidOptions(apiKey: "sk-12345")));
    }

    [Fact]
    public void CreateLlmProvider_ValidOptions_ReturnsProvider()
    {
        var factory = new ProviderFactory();
        using var provider = factory.CreateLlmProvider(ValidOptions());

        Assert.NotNull(provider);
        Assert.IsType<OpenAICompatibleLlmProvider>(provider);
    }

    [Fact]
    public void CreateLlmProvider_NullOptions_ReturnsNull()
    {
        var factory = new ProviderFactory();
        var provider = factory.CreateLlmProvider(null);

        Assert.Null(provider);
    }

    [Fact]
    public void CreateLlmProvider_LiteralApiKey_ThrowsConfigurationException()
    {
        var factory = new ProviderFactory();

        Assert.Throws<ConfigurationException>(
            () => factory.CreateLlmProvider(ValidOptions(apiKey: "AIzaSyTest123")));
    }
}
