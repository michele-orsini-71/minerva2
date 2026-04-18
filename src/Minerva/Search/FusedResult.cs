using Minerva.Storage;

namespace Minerva.Search;

public record FusedResult(ChunkSearchRecord Chunk, double Score);
