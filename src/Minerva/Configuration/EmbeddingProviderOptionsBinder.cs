using Microsoft.Extensions.Configuration;
using Minerva.Models;

namespace Minerva.Configuration;

public static class EmbeddingProviderOptionsBinder
{
    public static EmbeddingProviderOptions Bind(IConfiguration section)
    {
        var raw = new RawEmbeddingProviderOptions();
        section.Bind(raw);

        var failures = new List<OptionsFailure>();
        var result = TryBuild(raw, stagePrefix: "", failures);
        if (failures.Count > 0)
            throw new OptionsValidationException(failures);
        return result!;
    }

    internal static EmbeddingProviderOptions? TryBuild(
        RawEmbeddingProviderOptions raw, string stagePrefix, List<OptionsFailure> failures)
    {
        int before = failures.Count;

        BinderHelpers.ValidateAbsoluteUri(raw.BaseUrl, stagePrefix + "BaseUrl", failures);
        BinderHelpers.ValidateRequiredString(raw.Model, stagePrefix + "Model", failures);
        BinderHelpers.ValidateRequiredPositiveInt(raw.Concurrency, stagePrefix + "Concurrency", failures);
        BinderHelpers.ValidateRequiredPositiveInt(raw.BatchSize, stagePrefix + "BatchSize", failures);
        BinderHelpers.ValidateOptionalPositiveInt(raw.RequestsPerMinute, stagePrefix + "RequestsPerMinute", failures);

        if (failures.Count > before) return null;

        return new EmbeddingProviderOptions
        {
            BaseUrl = raw.BaseUrl!,
            Model = raw.Model!,
            ApiKey = string.IsNullOrWhiteSpace(raw.ApiKey) ? null : raw.ApiKey,
            Concurrency = raw.Concurrency!.Value,
            BatchSize = raw.BatchSize!.Value,
            RequestsPerMinute = raw.RequestsPerMinute,
        };
    }
}

internal sealed class RawEmbeddingProviderOptions
{
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
    public int? Concurrency { get; set; }
    public int? BatchSize { get; set; }
    public int? RequestsPerMinute { get; set; }
}
