namespace Minerva.Exceptions;

public sealed record InvariantDrift(string Field, string? Stored, string? Configured);

public sealed class CollectionConfigMismatchException : MinervaException
{
    public string CollectionName { get; }
    public IReadOnlyList<InvariantDrift> Drifts { get; }

    public CollectionConfigMismatchException(
        string collectionName,
        IReadOnlyList<InvariantDrift> drifts)
        : base(BuildMessage(collectionName, drifts))
    {
        CollectionName = collectionName;
        Drifts = drifts;
    }

    private static string BuildMessage(
        string collectionName, IReadOnlyList<InvariantDrift> drifts)
    {
        var fields = string.Join("; ", drifts.Select(d =>
            $"{d.Field} (stored '{d.Stored ?? "(none)"}', configured '{d.Configured ?? "(none)"}')"));
        return $"Collection '{collectionName}' was built with a different configuration. " +
               $"Re-ingesting would corrupt it. Drifted invariants: {fields}.";
    }
}
