namespace Minerva.Models;

public sealed record EmbeddingProviderOptions
{
    public required string BaseUrl { get; init; }
    public required string Model { get; init; }
    public string? ApiKey { get; init; }
    public required int Concurrency { get; init; }
    public required int BatchSize { get; init; }
    public int? RequestsPerMinute { get; init; }
}
