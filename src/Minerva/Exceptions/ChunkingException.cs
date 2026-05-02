namespace Minerva.Exceptions;

public sealed class ChunkingException : MinervaException
{
    public ChunkingException(string message) : base(message) { }
    public ChunkingException(string message, Exception inner) : base(message, inner) { }
}
