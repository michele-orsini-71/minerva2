using Minerva.Storage;

namespace Minerva.Search;

public record RankedChunk(ChunkSearchRecord Chunk, int Rank);
