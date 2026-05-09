using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Minerva.Configuration;

namespace Minerva.MarkdownIndexer;

public static partial class IndexerOptionsBinder
{
    private static readonly Regex CollectionNameRegex = CollectionNamePattern();

    public static IndexerOptions Bind(IConfiguration section)
    {
        var raw = new RawIndexerOptions();
        section.Bind(raw);

        var failures = new List<OptionsFailure>();
        var result = TryBuild(raw, stagePrefix: "", failures);
        if (failures.Count > 0)
            throw new OptionsValidationException(failures);
        return result!;
    }

    internal static IndexerOptions? TryBuild(
        RawIndexerOptions raw, string stagePrefix, List<OptionsFailure> failures)
    {
        int before = failures.Count;

        if (string.IsNullOrWhiteSpace(raw.RootPath))
            failures.Add(new OptionsFailure(stagePrefix + "RootPath", "is required."));

        if (string.IsNullOrWhiteSpace(raw.CollectionName))
            failures.Add(new OptionsFailure(stagePrefix + "CollectionName", "is required."));
        else if (!CollectionNameRegex.IsMatch(raw.CollectionName))
            failures.Add(new OptionsFailure(
                stagePrefix + "CollectionName",
                $"'{raw.CollectionName}' is invalid: must match ^[a-zA-Z0-9][a-zA-Z0-9-]*$."));

        if (raw.ExcludeDirectories is null)
            failures.Add(new OptionsFailure(stagePrefix + "ExcludeDirectories", "is required (use [] for none)."));

        if (failures.Count > before) return null;

        return new IndexerOptions
        {
            RootPath = raw.RootPath!,
            CollectionName = raw.CollectionName!,
            ExcludeDirectories = ImmutableArray.CreateRange(raw.ExcludeDirectories!),
        };
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9-]*$", RegexOptions.Compiled)]
    private static partial Regex CollectionNamePattern();
}

internal sealed class RawIndexerOptions
{
    public string? RootPath { get; set; }
    public string? CollectionName { get; set; }
    public string[]? ExcludeDirectories { get; set; }
}
