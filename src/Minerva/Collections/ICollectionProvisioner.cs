namespace Minerva.Collections;

internal interface ICollectionProvisioner
{
    Task EnsureHnswIndexAsync(string collectionName, int dimension, CancellationToken ct = default);
}
