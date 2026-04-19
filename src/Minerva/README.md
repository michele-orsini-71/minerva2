# Minerva

Core library. Provides a unified embedding, storage, and retrieval engine backed by PostgreSQL + pgvector.

The library is **client-agnostic**: it has no knowledge of Obsidian, Claude, markdown, or any specific data source. Clients (see `Minerva.MarkdownWatcher`) feed it `Document` objects and query it via `SearchAsync`.

## What it does

- **Ingestion**: chunk a document, embed each chunk, optionally summarize and contextualize via an LLM, upsert into PostgreSQL with content-hash deduplication.
- **Hybrid search**: dense vector similarity + full-text search, merged via Reciprocal Rank Fusion, with optional surrounding-context expansion.
- **Multi-collection**: each collection has its own embedding model and vector dimension; searches can span multiple collections.
- **Incremental updates**: re-ingesting a document with the same `SourceId` diffs by chunk hash — unchanged chunks are left alone.

## Public API

`MinervaEngine` is the facade:

```csharp
Task<IngestionResult> IngestAsync(string collection, Document doc, CancellationToken ct);
Task RemoveAsync(string collection, string sourceId, CancellationToken ct);
Task<IReadOnlyList<SearchResult>> SearchAsync(
    string query,
    IReadOnlyList<string> collections,
    SearchOptions? options,
    CancellationToken ct);
ICollectionService Collections { get; }   // CreateAsync, GetAsync, DeleteAsync, ListAsync
```

Key models (`Minerva.Models`):
- `Document(SourceId, Title, Text, Metadata?, Attachments?)`
- `SearchOptions(TopK, HybridAlpha, ExpandContext, MetadataFilter?)`
- `SearchResult(ChunkId, SourceId, CollectionName, Content, Score, Metadata?, ContextBefore?, ContextAfter?)`
- `IngestionResult(Added, Updated, Deleted, Unchanged)`

## Usage

```csharp
using Minerva.DI;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddMinerva(options =>
    builder.Configuration.GetSection("Minerva").Bind(options));

var host = builder.Build();
var engine = host.Services.GetRequiredService<IMinervaEngine>();

await engine.Collections.CreateAsync("my-notes", "nomic-embed-text", dimension: 768);
await engine.IngestAsync("my-notes", new Document("note-1", "Hello", "World"));

var hits = await engine.SearchAsync("world", ["my-notes"], new SearchOptions(TopK: 5));
```

`AddMinerva` registers everything, including a `MinervaStartupService` hosted service that runs schema initialization on startup.

## Configuration

```json
{
  "Minerva": {
    "ConnectionString": "Host=localhost;Database=minerva;Username=...;Password=...",
    "Embedding": {
      "BaseUrl": "http://localhost:11434/v1",
      "Model": "nomic-embed-text",
      "Concurrency": 1,
      "BatchSize": 1
    },
    "Llm": {
      "BaseUrl": "https://api.anthropic.com/v1",
      "Model": "claude-haiku-4-5",
      "ApiKey": "env:ANTHROPIC_API_KEY"
    },
    "Chunking": {
      "TargetChunkSize": 1200,
      "ChunkOverlap": 200,
      "EnableSummarization": false,
      "EnableContextualization": false
    }
  }
}
```

- `Llm` is optional — only required when `EnableSummarization` or `EnableContextualization` is `true`.
- API keys may be inlined or referenced via `env:VAR_NAME` (resolved by `CredentialResolver`).
- Embedding / LLM providers are any OpenAI-compatible HTTP endpoint.

### Configuration reference

Defaults are defined in `Configuration/MinervaOptions.cs` — that file is the source of truth.

**`Minerva`** (`MinervaOptions`)

| Field | Type | Default | Required? |
| --- | --- | --- | --- |
| `ConnectionString` | string | — | **required** (enforced by `required` keyword) |
| `Embedding` | `ProviderOptions` | — | **required** (enforced by `required` keyword; fields below) |
| `Llm` | `ProviderOptions?` | `null` | optional — required when any `Chunking.Enable*` flag is true |
| `Chunking` | `ChunkingOptions` | `new()` | optional (all fields have defaults) |

**`Embedding` / `Llm`** (`ProviderOptions`)

| Field | Type | Default | Required? |
| --- | --- | --- | --- |
| `BaseUrl` | string | — | **required** (enforced by `required` keyword) |
| `Model` | string | — | **required** (enforced by `required` keyword) |
| `ApiKey` | string? | `null` | optional (supports `env:VAR_NAME`) |
| `RequestsPerMinute` | int? | `null` (unlimited) | optional |
| `Concurrency` | int | `1` | optional |
| `BatchSize` | int | `1` | optional |

**`Chunking`** (`ChunkingOptions`)

| Field | Type | Default | Required? |
| --- | --- | --- | --- |
| `TargetChunkSize` | int | `1200` | optional |
| `ChunkOverlap` | int | `200` | optional |
| `EnableSummarization` | bool | `false` | optional (needs `Llm`) |
| `EnableContextualization` | bool | `false` | optional (needs `Llm`) |
| `LargeDocumentThreshold` | int | `8000` | optional |

## Internal layout

| Folder | Responsibility |
|---|---|
| `Collections/` | `CollectionManager`, `Collection` entity |
| `Configuration/` | `MinervaOptions`, `CredentialResolver` |
| `DI/` | `AddMinerva()` extension, `MinervaStartupService` |
| `Exceptions/` | Typed exceptions (`ConfigurationException`, etc.) |
| `Ingestion/` | `IngestionPipeline`, `DocumentChunker`, `EmbeddingService`, `DocumentSummarizer`, `ChunkContextualizer` |
| `Models/` | Public records (`Document`, `SearchResult`, …) |
| `Providers/` | OpenAI-compatible embedding/LLM clients, `RateLimiter`, `ProviderFactory` |
| `Search/` | `VectorSearch`, `FullTextSearch`, `ContextExpander`, `SearchPipeline` (RRF fusion) |
| `Storage/` | `SchemaInitializer`, `PostgresCollectionRepository`, `PostgresChunkRepository` |
| `Utilities/` | Cross-cutting helpers |
