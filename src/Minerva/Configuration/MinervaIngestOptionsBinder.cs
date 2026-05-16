using Microsoft.Extensions.Configuration;
using Minerva.Models;

namespace Minerva.Configuration;

public static class MinervaIngestOptionsBinder
{
    public static MinervaIngestOptions Bind(IConfiguration section)
    {
        var raw = new RawMinervaIngestOptions();
        section.Bind(raw);

        var failures = new List<OptionsFailure>();

        BinderHelpers.ValidateRequiredString(raw.ConnectionString, "ConnectionString", failures);

        EmbeddingProviderOptions? embedding = null;
        if (raw.Embedding is null)
            failures.Add(new OptionsFailure("Embedding", "section is required."));
        else
            embedding = EmbeddingProviderOptionsBinder.TryBuild(
                raw.Embedding, "Embedding.", failures);

        ChunkingOptions? chunking = null;
        if (raw.Chunking is null)
            failures.Add(new OptionsFailure("Chunking", "section is required."));
        else
            chunking = ChunkingOptionsBinder.TryBuild(
                raw.Chunking, "Chunking.", failures);

        if (failures.Count > 0)
            throw new OptionsValidationException(failures);

        return new MinervaIngestOptions
        {
            ConnectionString = raw.ConnectionString!,
            Embedding = embedding!,
            Chunking = chunking!,
        };
    }
}

internal sealed class RawMinervaIngestOptions
{
    public string? ConnectionString { get; set; }
    public RawEmbeddingProviderOptions? Embedding { get; set; }
    public RawChunkingOptions? Chunking { get; set; }
}
