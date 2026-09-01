using Minerva.Models;

namespace Minerva.Ingestion;

public static class AttachmentIntegrator
{
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
