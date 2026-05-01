using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minerva.Configuration;
using Minerva.Ingestion;

namespace Minerva.Readiness.Checks;

public sealed class LlmCallCheck : IReadinessCheck, IReadinessCheckTimeout
{
    private readonly MinervaOptions _options;
    private readonly ILlmAvailabilityProbe? _probe;
    private readonly ILogger<LlmCallCheck> _logger;

    public LlmCallCheck(
        IOptions<MinervaOptions> options,
        IServiceProvider serviceProvider,
        ILogger<LlmCallCheck> logger)
    {
        _options = options.Value;
        _probe = serviceProvider.GetService<ILlmAvailabilityProbe>();
        _logger = logger;
    }

    public string Name => nameof(LlmCallCheck);
    public ReadinessCategory Category => ReadinessCategory.Llm;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public async Task<ReadinessCheckResult> RunAsync(CancellationToken ct)
    {
        if (_options.Llm is null || _probe is null)
        {
            return new ReadinessCheckResult(
                Name, Category, Passed: true,
                Code: "MINERVA.LLM.NOT_CONFIGURED",
                Message: null,
                Remediation: null);
        }

        try
        {
            await _probe.CheckAvailabilityAsync(ct);
            return new ReadinessCheckResult(
                Name, Category, Passed: true,
                Code: "MINERVA.LLM.OK",
                Message: null,
                Remediation: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LLM availability probe failed");
            var endpoint = _options.Llm.BaseUrl;
            var model = _options.Llm.Model;
            return new ReadinessCheckResult(
                Name, Category, Passed: false,
                Code: "MINERVA.LLM.UNAVAILABLE",
                Message: Redact.Apply(ex.Message),
                Remediation: $"LLM at '{endpoint}' did not respond, or the model '{model}' is not available. Verify (1) the endpoint URL, (2) the API key, (3) that the model name matches what the provider exposes.");
        }
    }
}
