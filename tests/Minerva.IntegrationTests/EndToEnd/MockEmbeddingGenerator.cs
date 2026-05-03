using Minerva.Ingestion;

namespace Minerva.IntegrationTests.EndToEnd;

public sealed class MockEmbeddingGenerator : IEmbeddingClient, IEmbeddingDimensionProvider
{
    private readonly int _dimension;

    public MockEmbeddingGenerator(int dimension)
    {
        _dimension = dimension;
    }

    public Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var result = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
            result[i] = DeterministicUnitVector(texts[i], _dimension);
        return Task.FromResult<IReadOnlyList<float[]>>(result);
    }

    public Task<int> GetDimensionAsync(CancellationToken ct = default) =>
        Task.FromResult(_dimension);

    public void Dispose() { }

    private static float[] DeterministicUnitVector(string text, int dimension)
    {
        // Same input → same vector, so identical inputs have similarity 1.0
        // and different inputs sit far apart on the unit sphere.
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
