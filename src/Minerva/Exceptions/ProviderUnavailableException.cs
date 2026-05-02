namespace Minerva.Exceptions;

public sealed class ProviderUnavailableException : MinervaException
{
    public ProviderUnavailableException(string message) : base(message) { }
    public ProviderUnavailableException(string message, Exception inner) : base(message, inner) { }
}
