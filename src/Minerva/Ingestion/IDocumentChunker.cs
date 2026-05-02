using Minerva.Models;

namespace Minerva.Ingestion;

public interface IDocumentChunker
{
    IReadOnlyList<Chunk> Chunk(string collectionName, string sourceId, string text);
    IReadOnlyList<string> SegmentDocument(string text);
    IReadOnlyList<Chunk> ChunkSegment(
        string collectionName, string sourceId, string text, int startIndex);
}
