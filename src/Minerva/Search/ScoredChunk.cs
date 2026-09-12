using Minerva.Models;

namespace Minerva.Search;

internal record ScoredChunk(ChunkSearchRecord Chunk, double Score);
