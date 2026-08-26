using Minerva.Models;

namespace Minerva.Search.Bench.Sweep;

public static class CellOverrides
{
    public static SearchOverrides Build(Cell cell, SweepConfig configuration)
    {
        int topK = configuration.TopK;
        int candidatePoolSize = configuration.CandidatePoolSize;
        bool? enableReranker = null;

        double? hybridAlpha = null;

        foreach (var (knob, value) in cell.Values)
        {
            switch (knob)
            {
                case "enable_reranker":
                    enableReranker = Convert.ToBoolean(value);
                    break;
                case "hybrid_alpha":
                    hybridAlpha = Convert.ToDouble(value);
                    break;
                default:
                    throw new ArgumentException($"Unsupported knob '{knob}'.", nameof(cell));
            }
        }

        return new SearchOverrides { TopK = topK, HybridAlpha = hybridAlpha, EnableReranker = enableReranker, CandidatePoolSize = candidatePoolSize };
    }
}
