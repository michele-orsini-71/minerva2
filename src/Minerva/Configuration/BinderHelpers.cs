namespace Minerva.Configuration;

internal static class BinderHelpers
{
    public static void ValidateRequiredString(string? value, string path, List<OptionsFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
            failures.Add(new OptionsFailure(path, "is required."));
    }

    public static void ValidateAbsoluteUri(string? value, string path, List<OptionsFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(new OptionsFailure(path, "is required."));
            return;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out _))
            failures.Add(new OptionsFailure(path, $"is not a valid absolute URI: '{value}'."));
    }

    public static void ValidateRequiredPositiveInt(int? value, string path, List<OptionsFailure> failures)
    {
        if (value is null)
            failures.Add(new OptionsFailure(path, "is required."));
        else if (value <= 0)
            failures.Add(new OptionsFailure(path, $"must be > 0 (got {value})."));
    }

    public static void ValidateRequiredNonNegativeInt(int? value, string path, List<OptionsFailure> failures)
    {
        if (value is null)
            failures.Add(new OptionsFailure(path, "is required."));
        else if (value < 0)
            failures.Add(new OptionsFailure(path, $"must be >= 0 (got {value})."));
    }

    public static void ValidateOptionalPositiveInt(int? value, string path, List<OptionsFailure> failures)
    {
        if (value is int v && v <= 0)
            failures.Add(new OptionsFailure(path, $"must be > 0 when set (got {v})."));
    }
}
