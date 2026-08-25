using Minerva.Models;

namespace Minerva.Search;

public record ScoredChunk(ChunkSearchRecord Chunk, double Score);
