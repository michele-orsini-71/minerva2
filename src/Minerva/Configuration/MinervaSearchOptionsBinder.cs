using Microsoft.Extensions.Configuration;
using Minerva.Models;

namespace Minerva.Configuration;

public static class MinervaSearchOptionsBinder
{
    public static MinervaSearchOptions Bind(IConfiguration section)
    {
        var raw = new RawMinervaSearchOptions();
        section.Bind(raw);

        var failures = new List<OptionsFailure>();

        BinderHelpers.ValidateRequiredString(raw.ConnectionString, "ConnectionString", failures);

        EmbeddingProviderOptions? embedding = null;
        if (raw.Embedding is null)
            failures.Add(new OptionsFailure("Embedding", "section is required."));
        else
            embedding = EmbeddingProviderOptionsBinder.TryBuild(
                raw.Embedding, "Embedding.", failures);

        if (failures.Count > 0)
            throw new OptionsValidationException(failures);

        return new MinervaSearchOptions
        {
            ConnectionString = raw.ConnectionString!,
            Embedding = embedding!,
        };
    }
}

internal sealed class RawMinervaSearchOptions
{
    public string? ConnectionString { get; set; }
    public RawEmbeddingProviderOptions? Embedding { get; set; }
}
