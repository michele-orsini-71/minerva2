namespace Minerva.Models;

public record Document(
    string SourceId,
    string Title,
    string Text,
    Dictionary<string, object>? Metadata = null,
    Dictionary<string, AttachmentDescription>? Attachments = null);
