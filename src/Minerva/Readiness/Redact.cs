using System.Text.RegularExpressions;

namespace Minerva.Readiness;

internal static class Redact
{
    private static readonly Regex PasswordPattern = new(
        @"(?i)Password\s*=\s*[^;]+", RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        @"(?i)Bearer\s+\S+", RegexOptions.Compiled);

    public static string Apply(string? input)
    {
        if (input is null) return string.Empty;
        var step = PasswordPattern.Replace(input, "Password=***");
        return BearerPattern.Replace(step, "Bearer ***");
    }
}
