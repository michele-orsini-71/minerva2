namespace Minerva.Exceptions;

public sealed class EmbeddingException : MinervaException
{
    public EmbeddingException(string message) : base(message) { }
    public EmbeddingException(string message, Exception inner) : base(message, inner) { }
}
