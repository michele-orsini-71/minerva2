using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Minerva.Configuration;
using Minerva.Models;

namespace Minerva.Search.Bench.Sweep;

public static class RunJsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(
        string path,
        DateTimeOffset timestamp,
        string benchVersion,
        SweepConfig config,
        IReadOnlyList<Cell> cells,
        Collection collection,
        MinervaSearchOptions minervaSearchOptions)
    {
        var manifest = new Dictionary<string, object?>()
        {
            { "timestamp", timestamp.UtcDateTime.ToString(
                "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) },
            { "bench_version", benchVersion },
            { "dataset_path", config.Dataset },
            { "collection", config.Collection },
            { "collectionDetails", collection },
            { "reranker_model", minervaSearchOptions.Reranker?.Model },
            { "cascade_model", minervaSearchOptions.CascadeReranker?.Model },
            { "embedding_model", minervaSearchOptions.Embedding.Model },
            { "top_k", config.TopK },
            { "candidate_pool_size", config.CandidatePoolSize },
            { "resolved_sweep", new
            {
                top_k = config.TopK,
                candidate_pool_size = config.CandidatePoolSize,
                dataset = config.Dataset,
                collection = config.Collection,
                matrix = config.Matrix,
            } },
            { "cells", cells.Select(ToObject).ToList()},
        };

        if (!String.IsNullOrWhiteSpace(config.Label))
        {
            manifest["label"] = config.Label;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(manifest, Options));
    }

    private static Dictionary<string, object> ToObject(Cell cell)
    {
        var obj = new Dictionary<string, object>();
        foreach (var (knob, value) in cell.Values)
            obj[knob] = value;
        return obj;
    }
}
