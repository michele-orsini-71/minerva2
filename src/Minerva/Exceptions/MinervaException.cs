namespace Minerva.Exceptions;

public abstract class MinervaException : Exception
{
    protected MinervaException(string message) : base(message) { }
    protected MinervaException(string message, Exception inner) : base(message, inner) { }
}
