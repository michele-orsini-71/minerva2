using Minerva.Models;

namespace Minerva.Search.Bench.Sweep;

public static class CellOverrides
{
    public static SearchOverrides Build(Cell cell)
    {
        int? topK = null;
        double? hybridAlpha = null;

        foreach (var (knob, value) in cell.Values)
        {
            switch (knob)
            {
                case "top_k":
                    topK = Convert.ToInt32(value);
                    break;
                case "hybrid_alpha":
                    hybridAlpha = Convert.ToDouble(value);
                    break;
                default:
                    throw new ArgumentException($"Unsupported knob '{knob}'.", nameof(cell));
            }
        }

        return new SearchOverrides { TopK = topK, HybridAlpha = hybridAlpha };
    }
}
