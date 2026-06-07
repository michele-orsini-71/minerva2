namespace Minerva.Search.Bench.Sweep;

public sealed record Cell(IReadOnlyList<KeyValuePair<string, object>> Values);

public static class CellEnumerator
{
    public static IReadOnlyList<Cell> Enumerate(IReadOnlyDictionary<string, List<object>> matrix)
    {
        var cells = new List<List<KeyValuePair<string, object>>> { new() };

        foreach (var (knob, values) in matrix)
        {
            var next = new List<List<KeyValuePair<string, object>>>();
            foreach (var partial in cells)
                foreach (var value in values)
                    next.Add([.. partial, new KeyValuePair<string, object>(knob, value)]);
            cells = next;
        }

        return cells.Select(c => new Cell(c)).ToList();
    }
}
