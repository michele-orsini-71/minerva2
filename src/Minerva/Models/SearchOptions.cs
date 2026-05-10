namespace Minerva.Models;

public record SearchOptions(
    int TopK = 10,
    double HybridAlpha = 0.5,
    bool ExpandContext = false,
    Dictionary<string, object>? MetadataFilter = null,
    int CandidatePoolMultiplier = 5);
