using Minerva.Ingestion;
using Minerva.Models;

namespace Minerva.Tests.Ingestion;

[Trait("Category", "Ingestion")]
public class AttachmentIntegratorTests
{
    [Fact]
    public void Integrate_ReplacesKnownSyntaxWithDescription()
    {
        var text = "See this image: ![[photo.jpg]] and continue.";
        var attachments = new Dictionary<string, AttachmentDescription>
        {
            ["![[photo.jpg]]"] = new("A sunset over mountains"),
        };

        var result = AttachmentIntegrator.Integrate(text, attachments);

        Assert.Contains("![[photo.jpg]]", result);
        Assert.Contains("A sunset over mountains", result);
        Assert.Contains("and continue.", result);
    }

    [Fact]
    public void Integrate_MultipleAttachments()
    {
        var text = "![[a.png]] text ![[b.png]]";
        var attachments = new Dictionary<string, AttachmentDescription>
        {
            ["![[a.png]]"] = new("Image A"),
            ["![[b.png]]"] = new("Image B"),
        };

        var result = AttachmentIntegrator.Integrate(text, attachments);

        Assert.Contains("Image A", result);
        Assert.Contains("Image B", result);
    }

    [Fact]
    public void Integrate_EmptyAttachments_ReturnsTextUnchanged()
    {
        var text = "Hello ![[photo.jpg]] world";

        var result = AttachmentIntegrator.Integrate(text, []);

        Assert.Equal(text, result);
    }

    [Fact]
    public void Integrate_UnmatchedKey_TextUnchanged()
    {
        var text = "Hello world, no attachments here.";
        var attachments = new Dictionary<string, AttachmentDescription>
        {
            ["![[missing.jpg]]"] = new("A missing image"),
        };

        var result = AttachmentIntegrator.Integrate(text, attachments);

        Assert.Equal(text, result);
    }
}
