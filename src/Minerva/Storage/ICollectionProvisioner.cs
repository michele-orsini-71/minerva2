namespace Minerva.Storage;

public interface ICollectionProvisioner
{
    Task EnsureHnswIndexAsync(string collectionName, int dimension, CancellationToken ct = default);
}
