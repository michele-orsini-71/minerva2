using Minerva.Models;

namespace Minerva.Search;

internal record RankedChunk(ChunkSearchRecord Chunk, int Rank);
