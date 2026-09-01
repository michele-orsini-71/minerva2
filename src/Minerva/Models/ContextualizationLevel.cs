namespace Minerva.Models;

// Declaration order is escalation order: binder validation and provenance
// gating use range comparisons (<, >=) on these members.
public enum ContextualizationLevel
{
    None,
    Breadcrumb,
    DocumentBrief,
    PerChunk,
}
