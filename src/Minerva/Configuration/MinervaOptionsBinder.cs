using Microsoft.Extensions.Configuration;
using Minerva.Models;

namespace Minerva.Configuration;

public static class MinervaOptionsBinder
{
    public static MinervaOptions Bind(IConfiguration section)
    {
        var raw = new RawMinervaOptions();
        section.Bind(raw);

        var failures = new List<OptionsFailure>();
        var result = TryBuild(raw, stagePrefix: "", failures);
        if (failures.Count > 0)
            throw new OptionsValidationException(failures);
        return result!;
    }

    internal static MinervaOptions? TryBuild(
        RawMinervaOptions raw, string stagePrefix, List<OptionsFailure> failures)
    {
        int before = failures.Count;

        BinderHelpers.ValidateRequiredString(raw.ConnectionString, stagePrefix + "ConnectionString", failures);

        EmbeddingProviderOptions? embedding = null;
        if (raw.Embedding is null)
            failures.Add(new OptionsFailure(stagePrefix + "Embedding", "section is required."));
        else
            embedding = EmbeddingProviderOptionsBinder.TryBuild(
                raw.Embedding, stagePrefix + "Embedding.", failures);

        ChunkingOptions? chunking = null;
        if (raw.Chunking is null)
            failures.Add(new OptionsFailure(stagePrefix + "Chunking", "section is required."));
        else
            chunking = ChunkingOptionsBinder.TryBuild(
                raw.Chunking, stagePrefix + "Chunking.", failures);

        if (failures.Count > before) return null;

        return new MinervaOptions
        {
            ConnectionString = raw.ConnectionString!,
            Embedding = embedding!,
            Chunking = chunking!,
        };
    }
}

internal sealed class RawMinervaOptions
{
    public string? ConnectionString { get; set; }
    public RawEmbeddingProviderOptions? Embedding { get; set; }
    public RawChunkingOptions? Chunking { get; set; }
}
