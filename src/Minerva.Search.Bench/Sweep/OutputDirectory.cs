using System.Globalization;

namespace Minerva.Search.Bench.Sweep;

public static class OutputDirectory
{
    public static string Mint(string parentDir, DateTimeOffset timestamp, string datasetPath)
    {
        var slug = Path.GetFileNameWithoutExtension(datasetPath);
        var stamp = timestamp.UtcDateTime.ToString(
            "yyyy-MM-ddTHH-mm-ss'Z'", CultureInfo.InvariantCulture);
        var leaf = Path.Combine(parentDir, $"{stamp}_{slug}");

        if (Directory.Exists(leaf))
            throw new IOException($"Output directory already exists: {leaf}");

        Directory.CreateDirectory(leaf);
        return leaf;
    }
}
