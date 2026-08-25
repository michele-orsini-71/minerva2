namespace Minerva.Models;

public sealed record RerankerProviderOptions
{
    public required string BaseUrl { get; init; }
    public required string Model { get; init; }
}
