using Minerva.Models;

namespace Minerva.Search;

public record FusedResult(ChunkSearchRecord Chunk, double Score);
