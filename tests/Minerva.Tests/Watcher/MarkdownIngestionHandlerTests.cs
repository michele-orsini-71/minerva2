using Microsoft.Extensions.Logging.Abstractions;
using Minerva.MarkdownWatcher;
using Minerva.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Minerva.Tests.Watcher;

[Trait("Category", "Watcher")]
public class MarkdownIngestionHandlerTests : IDisposable
{
    private const string CollectionName = "notes";

    private readonly string _tempDir;
    private readonly IMinervaEngine _engine = Substitute.For<IMinervaEngine>();
    private readonly IMarkdownScanner _scanner = Substitute.For<IMarkdownScanner>();
    private readonly MarkdownIngestionHandler _handler;

    public MarkdownIngestionHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(),
            "minerva-handler-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _engine.IngestAsync(
                Arg.Any<string>(), Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(new IngestionResult(1, 0, 0, 0, TimeSpan.Zero));

        _handler = new MarkdownIngestionHandler(
            _engine, _scanner, CollectionName,
            NullLogger<MarkdownIngestionHandler>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string CreateFile(string name = "doc.md", string content = "# Title")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task OnChangedAsync_FileMissing_CallsDelete()
    {
        var path = Path.Combine(_tempDir, "missing.md");
        _scanner.DeriveSourceId(path).Returns("missing.md");

        await _handler.OnChangedAsync(path);

        await _engine.Received(1).RemoveAsync(
            CollectionName, "missing.md", Arg.Any<CancellationToken>());
        await _engine.DidNotReceive().IngestAsync(
            Arg.Any<string>(), Arg.Any<Document>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnChangedAsync_FileExcluded_DoesNothing()
    {
        var path = CreateFile();
        _scanner.IsExcluded(path).Returns(true);

        await _handler.OnChangedAsync(path);

        await _engine.DidNotReceive().IngestAsync(
            Arg.Any<string>(), Arg.Any<Document>(), Arg.Any<CancellationToken>());
        await _engine.DidNotReceive().RemoveAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnChangedAsync_ValidFile_IngestsDocument()
    {
        var path = CreateFile();
        _scanner.IsExcluded(path).Returns(false);
        var doc = new Document("doc.md", "Title", "# Title");
        _scanner.ReadFile(path).Returns(doc);

        await _handler.OnChangedAsync(path);

        await _engine.Received(1).IngestAsync(
            CollectionName, doc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnChangedAsync_IngestThrows_ExceptionSwallowed()
    {
        var path = CreateFile();
        _scanner.IsExcluded(path).Returns(false);
        _scanner.ReadFile(path).Returns(new Document("doc.md", "T", "x"));
        _engine.IngestAsync(
                Arg.Any<string>(), Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Must not throw.
        await _handler.OnChangedAsync(path);
    }

    [Fact]
    public async Task OnChangedAsync_Cancelled_PropagatesOperationCanceled()
    {
        var path = CreateFile();
        _scanner.IsExcluded(path).Returns(false);
        _scanner.ReadFile(path).Returns(new Document("doc.md", "T", "x"));
        _engine.IngestAsync(
                Arg.Any<string>(), Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _handler.OnChangedAsync(path));
    }

    [Fact]
    public async Task OnDeletedAsync_DerivesSourceAndRemoves()
    {
        var path = Path.Combine(_tempDir, "gone.md");
        _scanner.DeriveSourceId(path).Returns("gone.md");

        await _handler.OnDeletedAsync(path);

        await _engine.Received(1).RemoveAsync(
            CollectionName, "gone.md", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnDeletedAsync_RemoveThrows_ExceptionSwallowed()
    {
        var path = Path.Combine(_tempDir, "gone.md");
        _scanner.DeriveSourceId(path).Returns("gone.md");
        _engine.RemoveAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Must not throw.
        await _handler.OnDeletedAsync(path);
    }
}
