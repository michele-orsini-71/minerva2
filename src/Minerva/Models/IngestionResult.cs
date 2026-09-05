namespace Minerva.Models;

public record IngestionResult(
    int Added,
    int Updated,
    int Deleted,
    int Unchanged,
    TimeSpan Elapsed,
    int Failed);
