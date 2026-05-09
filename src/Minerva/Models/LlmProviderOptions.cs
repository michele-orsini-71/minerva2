namespace Minerva.Models;

public sealed record LlmProviderOptions
{
    public required string BaseUrl { get; init; }
    public required string Model { get; init; }
    public string? ApiKey { get; init; }
    public required int Concurrency { get; init; }
    public int? RequestsPerMinute { get; init; }
}
