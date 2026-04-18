using System.Text.RegularExpressions;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Storage;

namespace Minerva.Collections;

public partial class CollectionManager
{
    private static readonly Regex NamePattern = CollectionNameRegex();
    private static readonly Regex LiteralKeyPattern = LiteralApiKeyRegex();

    private readonly ICollectionRepository _collectionRepository;
    private readonly SchemaInitializer _schemaInitializer;

    public CollectionManager(
        ICollectionRepository collectionRepository,
        SchemaInitializer schemaInitializer)
    {
        _collectionRepository = collectionRepository;
        _schemaInitializer = schemaInitializer;
    }

    public async Task<Collection> CreateAsync(
        string name,
        string embeddingModel,
        int embeddingDimension,
        string? description = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        ValidateName(name);
        ValidateNoLiteralApiKeys(metadata);

        if (string.IsNullOrWhiteSpace(embeddingModel))
            throw new ConfigurationException("Embedding model must be a non-empty string.");
        if (embeddingDimension <= 0)
            throw new ConfigurationException("Embedding dimension must be positive.");

        if (await _collectionRepository.GetAsync(name, ct) is not null)
            throw new ConfigurationException($"Collection '{name}' already exists.");

        var collection = new Collection(
            Name: name,
            Description: description,
            EmbeddingModel: embeddingModel,
            EmbeddingDimension: embeddingDimension,
            Metadata: metadata);

        await _collectionRepository.CreateAsync(collection, ct);
        await _schemaInitializer.EnsureHnswIndexAsync(name, embeddingDimension, ct);

        return (await _collectionRepository.GetAsync(name, ct)) ?? collection;
    }

    public Task<Collection?> GetAsync(string name, CancellationToken ct = default) =>
        _collectionRepository.GetAsync(name, ct);

    public Task<IReadOnlyList<Collection>> ListAsync(CancellationToken ct = default) =>
        _collectionRepository.ListAsync(ct);

    public Task DeleteAsync(string name, CancellationToken ct = default) =>
        _collectionRepository.DeleteAsync(name, ct);

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ConfigurationException("Collection name must be non-empty.");
        if (!NamePattern.IsMatch(name))
            throw new ConfigurationException(
                $"Collection name '{name}' is invalid: must be alphanumeric or hyphens.");
    }

    private static void ValidateNoLiteralApiKeys(Dictionary<string, object>? metadata)
    {
        if (metadata is null) return;
        foreach (var value in metadata.Values)
        {
            if (value is string s && LiteralKeyPattern.IsMatch(s))
                throw new ConfigurationException(
                    "Collection metadata must not contain literal API keys.");
        }
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9-]*$")]
    private static partial Regex CollectionNameRegex();

    [GeneratedRegex(@"^(sk-|AIza|key-)", RegexOptions.IgnoreCase)]
    private static partial Regex LiteralApiKeyRegex();
}
