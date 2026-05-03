using Minerva.Exceptions;
using Minerva.Models;

namespace Minerva.Collections;

public interface ICollectionService
{
    Task<Collection> CreateAsync(
        string name,
        string embeddingModel,
        int embeddingDimension,
        string? description = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default);

    Task<Collection?> GetAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Collection>> ListAsync(CancellationToken ct = default);
    Task DeleteAsync(string name, CancellationToken ct = default);

    Task<Collection> EnsureAsync(
        string name,
        string? description = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default);
}
