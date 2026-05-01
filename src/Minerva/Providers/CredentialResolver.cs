using System.Text.RegularExpressions;
using Minerva.Exceptions;

namespace Minerva.Providers;

public sealed partial class CredentialResolver
{
    private static readonly Regex EnvVarPattern = EnvVarRegex();
    private static readonly Regex LiteralKeyPattern = LiteralKeyRegex();

    private readonly string _value;

    public CredentialResolver(string value)
    {
        if (LiteralKeyPattern.IsMatch(value))
            throw new ConfigurationException(
                "Literal API keys are not allowed. Use ${ENV_VAR} syntax to reference environment variables.");
        _value = value;
    }

    public string Resolve()
    {
        var match = EnvVarPattern.Match(_value);
        if (!match.Success) return _value;

        var envVar = match.Groups[1].Value;
        return Environment.GetEnvironmentVariable(envVar)
            ?? throw new ConfigurationException($"Environment variable '{envVar}' is not set.");
    }

    [GeneratedRegex(@"^\$\{(\w+)\}$")]
    private static partial Regex EnvVarRegex();

    [GeneratedRegex(@"^(sk-|AIza|key-)", RegexOptions.IgnoreCase)]
    private static partial Regex LiteralKeyRegex();
}
