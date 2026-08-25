using Microsoft.Extensions.Configuration;
using Minerva.Models;

namespace Minerva.Configuration;

public static class RerankerProviderOptionsBinder {
    public static RerankerProviderOptions Bind(IConfiguration section)
    {
        var raw = new RawRerankerProviderOptions();
        section.Bind(raw);

        var failures = new List<OptionsFailure>();
        var result = TryBuild(raw, stagePrefix: "", failures);
        if (failures.Count > 0)
            throw new OptionsValidationException(failures);
        return result!;
    }

    internal static RerankerProviderOptions? TryBuild(
        RawRerankerProviderOptions raw, string stagePrefix, List<OptionsFailure> failures)
    {
        int before = failures.Count;

        BinderHelpers.ValidateAbsoluteUri(raw.BaseUrl, stagePrefix + "BaseUrl", failures);
        BinderHelpers.ValidateRequiredString(raw.Model, stagePrefix + "Model", failures);

        if (failures.Count > before) return null;

        return new RerankerProviderOptions
        {
            BaseUrl = raw.BaseUrl!,
            Model = raw.Model!
        };
    }
}

internal sealed class RawRerankerProviderOptions
{
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
}
