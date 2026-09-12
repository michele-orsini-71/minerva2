using Minerva.Models;

namespace Minerva.Ingestion;

internal static class AttachmentIntegrator
{
    /// <summary>
    /// Replaces each attachment key found in text with: key + "\n" + description.
    /// Returns the original text unchanged if attachments is null or empty.
    /// </summary>
    public static string Integrate(string text, Dictionary<string, AttachmentDescription> attachments)
    {
        if (attachments.Count == 0)
            return text;

        foreach (var (key, attachment) in attachments)
        {
            if (text.Contains(key, StringComparison.Ordinal))
                text = text.Replace(key, $"{key}\n{attachment.Description}", StringComparison.Ordinal);
        }

        return text;
    }
}
