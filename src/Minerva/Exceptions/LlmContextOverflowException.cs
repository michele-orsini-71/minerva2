namespace Minerva.Exceptions;

public class LlmContextOverflowException : ProviderUnavailableException
{
    public int InputChars { get; init; }
    public string? ServerResponseBody { get; init; }
    public int? SegmentIndex { get; init; }
    public int? TotalSegments { get; init; }
    public string? DocumentPath { get; init; }
    public int? Budget { get; init; }

    public LlmContextOverflowException(string message) : base(message) { }
    public LlmContextOverflowException(string message, Exception inner) : base(message, inner) { }
}
