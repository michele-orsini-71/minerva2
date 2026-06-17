using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Minerva.Configuration;

namespace Minerva.MarkdownIndexer;

public static partial class IndexerOptionsBinder
{
    private static readonly Regex CollectionNameRegex = CollectionNamePattern();
    private static readonly Regex ExtensionRegex = ExtensionPattern();

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

        if (raw.FileExtensions is null)
            failures.Add(new OptionsFailure(
                stagePrefix + "FileExtensions", "is required (e.g. [\"md\", \"txt\"])."));
        else if (raw.FileExtensions.Length == 0)
            failures.Add(new OptionsFailure(
                stagePrefix + "FileExtensions", "must list at least one extension; an empty list selects no files."));
        else
            for (int i = 0; i < raw.FileExtensions.Length; i++)
            {
                var ext = raw.FileExtensions[i];
                if (string.IsNullOrWhiteSpace(ext) || !ExtensionRegex.IsMatch(ext))
                    failures.Add(new OptionsFailure(
                        stagePrefix + $"FileExtensions[{i}]",
                        $"'{ext}' is invalid: use a bare extension like 'md' or '.md', not a glob or pattern."));
            }

        if (raw.AllowRecreateOnConfigMismatch is null)
            failures.Add(new OptionsFailure(
                stagePrefix + "AllowRecreateOnConfigMismatch",
                "is required (true to permit dropping a collection whose build configuration no longer matches)."));

        if (raw.AllowSourceScopeChange is null)
            failures.Add(new OptionsFailure(
                stagePrefix + "AllowSourceScopeChange",
                "is required (true to allow reindexing a collection even if its source scope — root path, "
                + "excluded directories or file extensions — no longer matches)."));

        if (failures.Count > before) return null;

        return new IndexerOptions
        {
            RootPath = raw.RootPath!,
            CollectionName = raw.CollectionName!,
            ExcludeDirectories = [.. raw.ExcludeDirectories!],
            FileExtensions = [.. raw.FileExtensions!],
            AllowRecreateOnConfigMismatch = raw.AllowRecreateOnConfigMismatch!.Value,
            AllowSourceScopeChange = raw.AllowSourceScopeChange!.Value
        };
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9-]*$", RegexOptions.Compiled)]
    private static partial Regex CollectionNamePattern();

    [GeneratedRegex(@"^\.?[A-Za-z0-9]+$", RegexOptions.Compiled)]
    private static partial Regex ExtensionPattern();
}

internal sealed class RawIndexerOptions
{
    public string? RootPath { get; set; }
    public string? CollectionName { get; set; }
    public string[]? ExcludeDirectories { get; set; }
    public string[]? FileExtensions { get; set; }
    public bool? AllowRecreateOnConfigMismatch { get; set; }
    public bool? AllowSourceScopeChange { get; set; }
}
