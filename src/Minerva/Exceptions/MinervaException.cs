namespace Minerva.Exceptions;

public class MinervaException : Exception
{
    public MinervaException(string message) : base(message) { }
    public MinervaException(string message, Exception inner) : base(message, inner) { }
}

public class ConfigurationException : MinervaException
{
    public ConfigurationException(string message) : base(message) { }
    public ConfigurationException(string message, Exception inner) : base(message, inner) { }
}

public class IngestionException : MinervaException
{
    public IngestionException(string message) : base(message) { }
    public IngestionException(string message, Exception inner) : base(message, inner) { }
}

public class ChunkingException : IngestionException
{
    public ChunkingException(string message) : base(message) { }
    public ChunkingException(string message, Exception inner) : base(message, inner) { }
}

public class EmbeddingException : IngestionException
{
    public EmbeddingException(string message) : base(message) { }
    public EmbeddingException(string message, Exception inner) : base(message, inner) { }
}

public class StorageException : MinervaException
{
    public StorageException(string message) : base(message) { }
    public StorageException(string message, Exception inner) : base(message, inner) { }
}

public class SearchException : MinervaException
{
    public SearchException(string message) : base(message) { }
    public SearchException(string message, Exception inner) : base(message, inner) { }
}

public class ProviderUnavailableException : MinervaException
{
    public ProviderUnavailableException(string message) : base(message) { }
    public ProviderUnavailableException(string message, Exception inner) : base(message, inner) { }
}
