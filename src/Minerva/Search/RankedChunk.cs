using Minerva.Models;

namespace Minerva.Search;

public record RankedChunk(ChunkSearchRecord Chunk, int Rank);
