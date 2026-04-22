using Minerva.Models;

namespace Minerva.Collections;

public interface ICollectionRepository
{
    Task<Collection?> GetAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Collection>> ListAsync(CancellationToken ct = default);
    Task CreateAsync(Collection collection, CancellationToken ct = default);
    Task UpdateAsync(Collection collection, CancellationToken ct = default);
    Task DeleteAsync(string name, CancellationToken ct = default);
}
