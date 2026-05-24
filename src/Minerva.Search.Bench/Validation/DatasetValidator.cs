using System.Text.Json;
using Minerva.Search.Bench.Common;

namespace Minerva.Search.Bench.Validation;

public static class DatasetValidator
{
    private static readonly HashSet<string> AllowedKeys = new()
    {
        "id", "query", "gold_sources", "gold_sections", "answer_text", "notes"
    };

    public static PureValidationResult ValidatePure(TextReader reader)
    {
        var entries = new List<ParsedEntry>();
        var issues = new List<ValidationIssue>();
        var seenIds = new HashSet<string>();
        var lineNumber = 0;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                issues.Add(new ValidationIssue(lineNumber, null,
                    $"malformed JSON: {ex.Message}"));
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    issues.Add(new ValidationIssue(lineNumber, null,
                        $"expected a JSON object, got {root.ValueKind}"));
                    continue;
                }

                ValidateEntry(root, lineNumber, entries, issues, seenIds);
            }
        }

        return new PureValidationResult(entries, issues);
    }

    private static void ValidateEntry(
        JsonElement root, int lineNumber,
        List<ParsedEntry> entries, List<ValidationIssue> issues,
        HashSet<string> seenIds)
    {
        var entryId = TryGetEntryId(root);

        foreach (var prop in root.EnumerateObject())
        {
            if (!AllowedKeys.Contains(prop.Name))
                issues.Add(new ValidationIssue(lineNumber, entryId,
                    $"unknown top-level key: '{prop.Name}'"));
        }

        var id = ValidateId(root, lineNumber, issues, seenIds);
        var effectiveId = id ?? entryId;
        var query = ValidateQuery(root, lineNumber, effectiveId, issues);
        var goldSources = ValidateGoldSources(root, lineNumber, effectiveId, issues);
        ValidateGoldSectionsReserved(root, lineNumber, effectiveId, issues);

        if (id is not null && query is not null && goldSources.Count > 0)
        {
            entries.Add(new ParsedEntry(lineNumber, id, query, goldSources));
        }
    }

    private static string? TryGetEntryId(JsonElement root)
    {
        if (root.TryGetProperty("id", out var el) &&
            el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        return null;
    }

    private static string? ValidateId(
        JsonElement root, int lineNumber,
        List<ValidationIssue> issues, HashSet<string> seenIds)
    {
        if (!root.TryGetProperty("id", out var idEl))
        {
            issues.Add(new ValidationIssue(lineNumber, null, "missing required field: 'id'"));
            return null;
        }
        if (idEl.ValueKind != JsonValueKind.String)
        {
            issues.Add(new ValidationIssue(lineNumber, null,
                $"'id' must be a string, got {idEl.ValueKind}"));
            return null;
        }
        var value = idEl.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new ValidationIssue(lineNumber, null,
                "'id' must be a non-empty, non-whitespace string"));
            return null;
        }
        if (!seenIds.Add(value))
        {
            issues.Add(new ValidationIssue(lineNumber, value,
                $"duplicate id: '{value}' was already used in this dataset"));
            return null;
        }
        return value;
    }

    private static string? ValidateQuery(
        JsonElement root, int lineNumber, string? effectiveId,
        List<ValidationIssue> issues)
    {
        if (!root.TryGetProperty("query", out var qEl))
        {
            issues.Add(new ValidationIssue(lineNumber, effectiveId,
                "missing required field: 'query'"));
            return null;
        }
        if (qEl.ValueKind != JsonValueKind.String)
        {
            issues.Add(new ValidationIssue(lineNumber, effectiveId,
                $"'query' must be a string, got {qEl.ValueKind}"));
            return null;
        }
        var value = qEl.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new ValidationIssue(lineNumber, effectiveId,
                "'query' must be a non-empty, non-whitespace string"));
            return null;
        }
        return value;
    }

    private static IReadOnlyList<string> ValidateGoldSources(
        JsonElement root, int lineNumber, string? effectiveId,
        List<ValidationIssue> issues)
    {
        if (!root.TryGetProperty("gold_sources", out var gsEl))
        {
            issues.Add(new ValidationIssue(lineNumber, effectiveId,
                "missing required field: 'gold_sources'"));
            return Array.Empty<string>();
        }
        if (gsEl.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new ValidationIssue(lineNumber, effectiveId,
                $"'gold_sources' must be an array, got {gsEl.ValueKind}"));
            return Array.Empty<string>();
        }
        if (gsEl.GetArrayLength() == 0)
        {
            issues.Add(new ValidationIssue(lineNumber, effectiveId,
                "'gold_sources' must contain at least one entry"));
            return Array.Empty<string>();
        }

        var collected = new List<string>();
        var seenInEntry = new HashSet<string>();
        foreach (var element in gsEl.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                issues.Add(new ValidationIssue(lineNumber, effectiveId,
                    $"'gold_sources' entries must be strings, got {element.ValueKind}"));
                continue;
            }
            var value = element.GetString()!;
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new ValidationIssue(lineNumber, effectiveId,
                    "'gold_sources' entry must be a non-empty, non-whitespace string"));
                continue;
            }
            if (!seenInEntry.Add(value))
            {
                issues.Add(new ValidationIssue(lineNumber, effectiveId,
                    $"duplicate source_id within 'gold_sources': '{value}'"));
                continue;
            }
            collected.Add(value);
        }
        return collected;
    }

    private static void ValidateGoldSectionsReserved(
        JsonElement root, int lineNumber, string? effectiveId,
        List<ValidationIssue> issues)
    {
        if (!root.TryGetProperty("gold_sections", out var gsxEl))
            return;
        if (gsxEl.ValueKind == JsonValueKind.Array && gsxEl.GetArrayLength() == 0)
            return;
        issues.Add(new ValidationIssue(lineNumber, effectiveId,
            "'gold_sections' is reserved for Phase 4 and must not be populated"));
    }
}
