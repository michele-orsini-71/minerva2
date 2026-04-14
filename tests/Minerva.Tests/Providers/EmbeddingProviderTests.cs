using Minerva.Providers;

namespace Minerva.Tests.Providers;

[Trait("Category", "Providers")]
public class EmbeddingProviderTests
{
    [Fact]
    public void L2Normalize_ProducesUnitVector()
    {
        float[] vector = [3f, 4f];
        OpenAICompatibleEmbeddingProvider.L2Normalize(vector);

        float norm = MathF.Sqrt(vector[0] * vector[0] + vector[1] * vector[1]);
        Assert.InRange(norm, 0.999f, 1.001f);
    }

    [Fact]
    public void L2Normalize_KnownVector_CorrectResult()
    {
        // [3, 4] normalized: [3/5, 4/5] = [0.6, 0.8]
        float[] vector = [3f, 4f];
        OpenAICompatibleEmbeddingProvider.L2Normalize(vector);

        Assert.InRange(vector[0], 0.599f, 0.601f);
        Assert.InRange(vector[1], 0.799f, 0.801f);
    }

    [Fact]
    public void L2Normalize_ZeroVector_RemainsZero()
    {
        float[] vector = [0f, 0f, 0f];
        OpenAICompatibleEmbeddingProvider.L2Normalize(vector);

        Assert.All(vector, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void L2Normalize_SingleElement_BecomesOne()
    {
        float[] vector = [5f];
        OpenAICompatibleEmbeddingProvider.L2Normalize(vector);

        Assert.InRange(vector[0], 0.999f, 1.001f);
    }

    [Fact]
    public void L2Normalize_HighDimensional_ProducesUnitVector()
    {
        var random = new Random(42);
        float[] vector = Enumerable.Range(0, 1536)
            .Select(_ => (float)(random.NextDouble() * 2 - 1))
            .ToArray();

        OpenAICompatibleEmbeddingProvider.L2Normalize(vector);

        float norm = MathF.Sqrt(vector.Sum(v => v * v));
        Assert.InRange(norm, 0.999f, 1.001f);
    }

    [Fact]
    public void L2Normalize_AlreadyNormalized_RemainsNormalized()
    {
        // Unit vector in 3D
        float sqrt3 = MathF.Sqrt(3f);
        float[] vector = [1f / sqrt3, 1f / sqrt3, 1f / sqrt3];

        OpenAICompatibleEmbeddingProvider.L2Normalize(vector);

        float norm = MathF.Sqrt(vector.Sum(v => v * v));
        Assert.InRange(norm, 0.999f, 1.001f);
    }
}
