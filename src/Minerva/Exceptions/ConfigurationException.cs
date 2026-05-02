namespace Minerva.Exceptions;

public sealed class ConfigurationException : MinervaException
{
    public ConfigurationException(string message) : base(message) { }
    public ConfigurationException(string message, Exception inner) : base(message, inner) { }
}
