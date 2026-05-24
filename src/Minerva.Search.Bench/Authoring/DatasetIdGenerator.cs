using System.Text.RegularExpressions;

namespace Minerva.Search.Bench.Authoring;

public static class DatasetIdGenerator
{
    public static string GenerateId(string sourceId, ISet<string> existingIds)
    {
        var regex = new Regex($"^{Regex.Escape(sourceId)}-(\\d+)$");
        var existingIdsForSource = existingIds.Where(id => regex.IsMatch(id)).ToList();
        if (existingIdsForSource.Count == 0)
        {
            return $"{sourceId}-1";
        }

        var maxSuffix = existingIdsForSource.Max(id => int.Parse(regex.Match(id).Groups[1].Value));
        return $"{sourceId}-{maxSuffix + 1}";
    }
}