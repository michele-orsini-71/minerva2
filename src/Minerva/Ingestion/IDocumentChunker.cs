using Minerva.Models;

namespace Minerva.Ingestion;

internal interface IDocumentChunker
{
    IReadOnlyList<Chunk> Chunk(string collectionName, string sourceId, string text);
}
