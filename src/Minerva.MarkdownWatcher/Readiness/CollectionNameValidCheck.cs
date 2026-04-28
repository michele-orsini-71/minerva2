using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Minerva.Readiness;

namespace Minerva.MarkdownWatcher.Readiness;

public sealed class CollectionNameValidCheck : IReadinessCheck, IReadinessCheckTimeout
{
    // Third copy of the regex by design — see PRD 05 / SchemaInitializer DDL-injection guard.
    private static readonly Regex CollectionNameRegex =
        new(@"^[a-zA-Z0-9][a-zA-Z0-9-]*$", RegexOptions.Compiled);

    private readonly WatcherOptions _options;

    public CollectionNameValidCheck(IOptions<WatcherOptions> options)
        : this(options.Value) { }

    internal CollectionNameValidCheck(WatcherOptions options)
    {
        _options = options;
    }

    public string Name => nameof(CollectionNameValidCheck);
    public ReadinessCategory Category => ReadinessCategory.Client;
    public TimeSpan Timeout => TimeSpan.FromSeconds(2);

    public Task<ReadinessCheckResult> RunAsync(CancellationToken ct)
    {
        var name = _options.CollectionName;

        if (string.IsNullOrWhiteSpace(name) || !CollectionNameRegex.IsMatch(name))
        {
            return Task.FromResult(new ReadinessCheckResult(
                Name, Category, Passed: false,
                Code: "MINERVA.CLIENT.COLLECTION_NAME_INVALID",
                Message: $"Collection name '{name}' is not valid.",
                Remediation: "Collection name must match ^[a-zA-Z0-9][a-zA-Z0-9-]*$ (start with a letter or digit; only letters, digits, hyphens)."));
        }

        return Task.FromResult(new ReadinessCheckResult(
            Name, Category, Passed: true,
            Code: "MINERVA.CLIENT.COLLECTION_NAME_OK",
            Message: null,
            Remediation: null));
    }
}
