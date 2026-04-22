using Microsoft.Extensions.Logging;

namespace Minerva.Ingestion;

public class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingClient _client;
    private readonly int _batchSize;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        IEmbeddingClient client,
        int batchSize,
        ILogger<EmbeddingService> logger)
    {
        _client = client;
        _batchSize = batchSize;
        _logger = logger;
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (texts.Count == 0)
            return [];

        var results = new float[texts.Count][];
        int embedded = 0;

        for (int i = 0; i < texts.Count; i += _batchSize)
        {
            int batchStart = i;
            var batch = texts.Skip(i).Take(_batchSize).ToList();

            try
            {
                var generated = await _client.EmbedAsync(batch, ct);

                for (int j = 0; j < generated.Count; j++)
                    results[batchStart + j] = generated[j];

                embedded += batch.Count;
                progress?.Report(embedded);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Batch embedding failed at offset {Offset}, falling back to individual embedding",
                    batchStart);

                // Fall back to embedding each text individually
                for (int j = 0; j < batch.Count; j++)
                {
                    var single = await _client.EmbedAsync([batch[j]], ct);
                    results[batchStart + j] = single[0];

                    embedded++;
                    progress?.Report(embedded);
                }
            }
        }

        return results;
    }
}
