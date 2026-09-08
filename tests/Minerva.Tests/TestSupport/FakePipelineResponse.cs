using System.ClientModel;
using System.ClientModel.Primitives;

namespace Minerva.Tests.TestSupport;

internal static class ClientResultExceptions
{
    // The (string) constructor leaves Status at 0; only a response carries a real status code.
    public static ClientResultException WithStatus(int status, string body = "") =>
        new(new FakePipelineResponse(status, body));
}

internal sealed class FakePipelineResponse : PipelineResponse
{
    private readonly BinaryData _content;

    public FakePipelineResponse(int status, string body)
    {
        Status = status;
        _content = BinaryData.FromString(body);
    }

    public override int Status { get; }
    public override string ReasonPhrase => string.Empty;
    public override Stream? ContentStream { get; set; }
    public override BinaryData Content => _content;
    protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
    public override bool IsError => Status >= 400;

    public override BinaryData BufferContent(CancellationToken cancellationToken = default) => _content;

    public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_content);

    public override void Dispose()
    {
    }
}
