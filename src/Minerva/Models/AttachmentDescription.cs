namespace Minerva.Models;

public record AttachmentDescription(
    string Description,
    string? SourcePath = null,
    Dictionary<string, object>? Metadata = null);
