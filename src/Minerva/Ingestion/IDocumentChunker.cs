using Minerva.Models;

namespace Minerva.Ingestion;

public interface IDocumentChunker
{
    IReadOnlyList<Chunk> Chunk(string collectionName, string sourceId, string text);
}
