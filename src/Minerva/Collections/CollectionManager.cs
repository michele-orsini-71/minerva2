using System.Text.RegularExpressions;
using Minerva.Exceptions;
using Minerva.Models;

namespace Minerva.Collections;

internal partial class CollectionManager : ICollectionService
{
    private static readonly Regex NamePattern = CollectionNameRegex();
    private static readonly Regex LiteralKeyPattern = LiteralApiKeyRegex();

    private readonly ICollectionRepository _collectionRepository;
    private readonly ICollectionProvisioner _provisioner;

    public CollectionManager(
        ICollectionRepository collectionRepository,
        ICollectionProvisioner provisioner)
    {
        _collectionRepository = collectionRepository;
        _provisioner = provisioner;
    }

    public async Task<Collection> CreateAsync(
        string name,
        CollectionProvenance provenance,
        ClientProvenance clientProvenance,
        string? description = null,
        CancellationToken ct = default)
    {
        ValidateName(name);
        ValidateProvenance(provenance);
        ValidateNoLiteralApiKeys(clientProvenance.data);

        if (await _collectionRepository.GetAsync(name, ct) is not null)
            throw new ConfigurationException($"Collection '{name}' already exists.");

        var collection = new Collection(
            Name: name,
            Description: description,
            Provenance: provenance,
            ClientProvenance: clientProvenance);


        await _collectionRepository.CreateAsync(collection, ct);
        await _provisioner.EnsureHnswIndexAsync(
            name, provenance.Invariants.EmbeddingDimension, ct);

        return (await _collectionRepository.GetAsync(name, ct)) ?? collection;
    }

    public Task<Collection?> GetAsync(string name, CancellationToken ct = default) =>
        _collectionRepository.GetAsync(name, ct);

    public Task<IReadOnlyList<Collection>> ListAsync(CancellationToken ct = default) =>
        _collectionRepository.ListAsync(ct);

    public Task DeleteAsync(string name, CancellationToken ct = default) =>
        _collectionRepository.DeleteAsync(name, ct);

    public async Task<Collection> EnsureAsync(
        string name,
        CollectionProvenance provenance,
        ClientProvenance clientProvenance,
        string? description = null,
        CancellationToken ct = default)
    {
        var existing = await _collectionRepository.GetAsync(name, ct);
        if (existing is not null)
            return existing;

        return await CreateAsync(name, provenance, clientProvenance, description, ct);
    }


    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ConfigurationException("Collection name must be non-empty.");
        if (!NamePattern.IsMatch(name))
            throw new ConfigurationException(
                $"Collection name '{name}' is invalid: must be alphanumeric or hyphens.");
    }


    private static void ValidateProvenance(CollectionProvenance provenance)
    {
        var invariants = provenance.Invariants;
        if (string.IsNullOrWhiteSpace(invariants.EmbeddingModel))
            throw new ConfigurationException("Embedding model must be a non-empty string.");
        if (invariants.EmbeddingDimension <= 0)
            throw new ConfigurationException("Embedding dimension must be positive.");
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
