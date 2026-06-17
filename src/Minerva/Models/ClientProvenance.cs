using System.Collections;
using Minerva.Exceptions;

namespace Minerva.Models;

public sealed record ClientProvenance
{
    public string kind { get; }
    public Dictionary<string, object> data { get; }

    public ClientProvenance(string kind, Dictionary<string, object> data)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(data);
        ValidateScalarBag(data);
        this.kind = kind;
        this.data = data;
    }

    // Provenance entries are simple, comparable annotations: a value is a scalar or a flat array
    // of scalars, never a nested structure. Consumers rely on this — for example, comparing two
    // provenance values as sets to detect a scope change only makes sense for scalars. The type
    // owns this invariant so every client gets the guarantee and no caller has to re-check it.
    private static void ValidateScalarBag(Dictionary<string, object> data)
    {
        foreach (var (key, value) in data)
        {
            if (IsScalar(value))
            {
                continue;
            }

            if (value is IEnumerable items and not string)
            {
                foreach (var item in items)
                {
                    if (!IsScalar(item))
                    {
                        throw new ClientProvenanceException(
                            $"Client provenance value '{key}' contains a non-scalar element " +
                            $"({item?.GetType().Name ?? "null"}); only scalars and arrays of scalars are allowed.");
                    }
                }
                continue;
            }

            throw new ClientProvenanceException(
                $"Client provenance value '{key}' is not a scalar or array of scalars " +
                $"({value?.GetType().Name ?? "null"}).");
        }
    }

    private static bool IsScalar(object? value) =>
        value is null or string or bool
            or sbyte or byte or short or ushort or int or uint or long or ulong
            or float or double or decimal;
}
