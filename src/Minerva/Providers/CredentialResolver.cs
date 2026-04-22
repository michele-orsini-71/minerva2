using System.Text.RegularExpressions;
using Minerva.Exceptions;

namespace Minerva.Providers;

public static partial class CredentialResolver
{
    private static readonly Regex EnvVarPattern = EnvVarRegex();
    private static readonly Regex LiteralKeyPattern = LiteralKeyRegex();

    public static string Resolve(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (LiteralKeyPattern.IsMatch(value))
            throw new ConfigurationException(
                "Literal API keys are not allowed. Use ${ENV_VAR} syntax to reference environment variables.");

        var match = EnvVarPattern.Match(value);
        if (!match.Success)
            return value;

        var envVar = match.Groups[1].Value;
        var resolved = Environment.GetEnvironmentVariable(envVar)
            ?? throw new ConfigurationException($"Environment variable '{envVar}' is not set.");

        return resolved;
    }

    [GeneratedRegex(@"^\$\{(\w+)\}$")]
    private static partial Regex EnvVarRegex();

    [GeneratedRegex(@"^(sk-|AIza|key-)", RegexOptions.IgnoreCase)]
    private static partial Regex LiteralKeyRegex();
}
