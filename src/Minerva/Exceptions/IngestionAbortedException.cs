namespace Minerva.Exceptions;

public class IngestionAbortedException : MinervaException
{
    public IngestionAbortedException(
        string message, Exception inner, int ingested, int failed, TimeSpan elapsed)
        : base(message, inner)
    {
        Ingested = ingested;
        Failed = failed;
        Elapsed = elapsed;
    }

    public int Ingested { get; }
    public int Failed { get; }
    public TimeSpan Elapsed { get; }
}
