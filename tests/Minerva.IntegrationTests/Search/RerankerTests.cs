using Minerva.Providers;

namespace Minerva.IntegrationTests.Search;

// Requires a running llama-server with the reranking endpoint enabled:
//   llama-server -hf gpustack/bge-reranker-v2-m3-GGUF:Q8_0 --reranking --port 9932
// URL comes from MINERVA_TEST_RERANKER_URL (.runsettings), default http://127.0.0.1:9932.
[Trait("Category", "Reranker")]
public class RerankerTests
{
    private const string ModelId = "gpustack/bge-reranker-v2-m3-GGUF:Q8_0";

    private static Uri RerankerUri =>
        new(Environment.GetEnvironmentVariable("MINERVA_TEST_RERANKER_URL") ?? "http://127.0.0.1:9932");

    [Fact]
    public async Task RankTexts_RanksTheRelevantDocumentTop_AtItsInputPosition()
    {
        var provider = new HttpRerankerProvider(ModelId, RerankerUri);
        var query = "What is the largest animal phylum?";
        var documents = new List<string>
        {
            "A good risotto needs constant stirring and a splash of white wine.",           // 0: irrelevant
            "Arthropods are the largest phylum in the animal kingdom — insects, "
                + "arachnids and crustaceans all belong to it.",                            // 1: relevant
            "The Baroque period in music followed the Renaissance.",                        // 2: irrelevant
        };

        var scores = await provider.RankTexts(query, documents, CancellationToken.None);

        // One score per document, aligned to input order.
        Assert.Equal(documents.Count, scores.Length);

        // The relevant document sits at input index 1; its score must be the highest.
        // This fails if the provider returns scores in the server's sorted order
        // instead of mapping each score back to its input position via `index`.
        var topIndex = Array.IndexOf(scores, scores.Max());
        Assert.Equal(1, topIndex);
    }
}
