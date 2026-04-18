using Microsoft.Extensions.AI;

namespace Minerva.IntegrationTests.EndToEnd;

/// <summary>
/// Deterministic pseudo-random unit-vector embeddings keyed off the input text.
/// Same input → same vector, so semantic similarity between duplicate inputs is 1.0
/// and between different inputs is low.
/// </summary>
public sealed class MockEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly int _dimension;

    public MockEmbeddingGenerator(int dimension)
    {
        _dimension = dimension;
        Metadata = new EmbeddingGeneratorMetadata(nameof(MockEmbeddingGenerator), null, "mock-embedding");
    }

    public EmbeddingGeneratorMetadata Metadata { get; }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var value in values)
            result.Add(new Embedding<float>(DeterministicUnitVector(value, _dimension)));
        return Task.FromResult(result);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null) return null;
        if (serviceType == typeof(EmbeddingGeneratorMetadata)) return Metadata;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose() { }

    private static float[] DeterministicUnitVector(string text, int dimension)
    {
        // Seed per-input so repeated ingests of identical text produce the same vector.
        var rng = new Random(text.GetHashCode(StringComparison.Ordinal));
        var vector = new float[dimension];
        float sumSquares = 0;
        for (int i = 0; i < dimension; i++)
        {
            vector[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
            sumSquares += vector[i] * vector[i];
        }

        if (sumSquares > 0)
        {
            var norm = MathF.Sqrt(sumSquares);
            for (int i = 0; i < dimension; i++)
                vector[i] /= norm;
        }

        return vector;
    }
}
