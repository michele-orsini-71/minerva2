namespace Minerva.Models;

public class ProviderOptions
{
    public required string BaseUrl { get; set; }
    public required string Model { get; set; }
    public string? ApiKey { get; set; }
    public int? RequestsPerMinute { get; set; }
    public int Concurrency { get; set; } = 1;
    public int BatchSize { get; set; } = 1;
}
