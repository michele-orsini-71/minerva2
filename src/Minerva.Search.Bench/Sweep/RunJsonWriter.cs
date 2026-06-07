using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

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
        IReadOnlyList<Cell> cells)
    {
        var manifest = new
        {
            timestamp = timestamp.UtcDateTime.ToString(
                "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            bench_version = benchVersion,
            dataset_path = config.Dataset,
            collection = config.Collection,
            resolved_sweep = new
            {
                dataset = config.Dataset,
                collection = config.Collection,
                matrix = config.Matrix,
            },
            cells = cells.Select(ToObject).ToList(),
        };

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
