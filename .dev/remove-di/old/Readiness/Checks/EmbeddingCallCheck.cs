using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minerva.Configuration;
using Minerva.Ingestion;

namespace Minerva.Readiness.Checks;

public sealed class EmbeddingCallCheck : IReadinessCheck, IReadinessCheckTimeout
{
    private readonly MinervaOptions _options;
    private readonly IEmbeddingDimensionProvider? _dimensionProvider;
    private readonly ILogger<EmbeddingCallCheck> _logger;

    public EmbeddingCallCheck(
        IOptions<MinervaOptions> options,
        IServiceProvider serviceProvider,
        ILogger<EmbeddingCallCheck> logger)
    {
        _options = options.Value;
        _dimensionProvider = serviceProvider.GetService<IEmbeddingDimensionProvider>();
        _logger = logger;
    }

    public string Name => nameof(EmbeddingCallCheck);
    public ReadinessCategory Category => ReadinessCategory.Embedding;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public async Task<ReadinessCheckResult> RunAsync(CancellationToken ct)
    {
        if (_options.Embedding is null || _dimensionProvider is null)
        {
            return new ReadinessCheckResult(
                Name, Category, Passed: true,
                Code: "MINERVA.EMBEDDING.NOT_CONFIGURED",
                Message: null,
                Remediation: null);
        }

        try
        {
            await _dimensionProvider.GetDimensionAsync(ct);
            return new ReadinessCheckResult(
                Name, Category, Passed: true,
                Code: "MINERVA.EMBEDDING.OK",
                Message: null,
                Remediation: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Embedding probe failed");
            var endpoint = _options.Embedding.BaseUrl;
            var model = _options.Embedding.Model;
            return new ReadinessCheckResult(
                Name, Category, Passed: false,
                Code: "MINERVA.EMBEDDING.UNAVAILABLE",
                Message: Redact.Apply(ex.Message),
                Remediation: $"Embedder at '{endpoint}' did not respond, or the model '{model}' is not available. Verify (1) the endpoint URL, (2) the API key, (3) that the model name matches what the provider exposes. For local stacks (Ollama / LM Studio), confirm the model is loaded — see the README for the keep-loaded settings.");
        }
    }
}
