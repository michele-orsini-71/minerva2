namespace Minerva.Exceptions;

public sealed class CollectionEmbedderMismatchException : MinervaException
{
    public string CollectionName { get; }
    public string StoredModel { get; }
    public int StoredDimension { get; }
    public string ConfiguredModel { get; }
    public int ConfiguredDimension { get; }

    public CollectionEmbedderMismatchException(
        string collectionName,
        string storedModel,
        int storedDimension,
        string configuredModel,
        int configuredDimension)
        : base(BuildMessage(
            collectionName, storedModel, storedDimension, configuredModel, configuredDimension))
    {
        CollectionName = collectionName;
        StoredModel = storedModel;
        StoredDimension = storedDimension;
        ConfiguredModel = configuredModel;
        ConfiguredDimension = configuredDimension;
    }

    private static string BuildMessage(
        string collectionName,
        string storedModel,
        int storedDimension,
        string configuredModel,
        int configuredDimension) =>
        $"Collection '{collectionName}' was created with embedder '{storedModel}' " +
        $"(dim {storedDimension}), but engine is configured with '{configuredModel}' " +
        $"(dim {configuredDimension}).";
}
