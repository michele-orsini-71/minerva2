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

await engine.Collections.CreateAsync("my-notes", "embedding-bge-m3", dimension: 768);
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
      "Model": "embedding-bge-m3",
      "Concurrency": 1,
      "BatchSize": 1
    },
    "Chunking": {
      "TargetChunkSize": 1200,
      "ChunkOverlap": 200,
      "MaxSegmentChars": 8000,
      "ChunkerType": "Custom",
      "Llm": {
        "BaseUrl": "https://api.anthropic.com/v1",
        "Model": "claude-haiku-4-5",
        "ApiKey": "env:ANTHROPIC_API_KEY",
        "Concurrency": 1
      }
    }
  }
}
```

- `Chunking.Llm` is optional. When **present**, large documents are summarized and chunks are contextualized via that LLM. When **absent**, large documents are still segmented (using `MaxSegmentChars`) but the LLM-driven steps are skipped — there is no separate enable/disable flag, presence is the toggle.
- API keys may be inlined or referenced via `env:VAR_NAME` (resolved by `CredentialResolver`).
- Embedding / LLM providers are any OpenAI-compatible HTTP endpoint.

> **Local-runtime tip.** When both `Embedding` and `Chunking.Llm` point at the same local runtime (Ollama, LM Studio, …), keep both models resident — otherwise every ingestion alternates between embedding and summarization/contextualization calls and the runtime swaps models in and out of VRAM on each switch.
>
> - **Ollama**: set `OLLAMA_MAX_LOADED_MODELS=2` (or higher) and a generous `OLLAMA_KEEP_ALIVE` (e.g. `24h`).
> - **LM Studio**: load both models in the _Models_ panel before starting the client.

### Configuration reference

Defaults are defined in `Configuration/MinervaOptions.cs` — that file is the source of truth.

**`Minerva`** (`MinervaOptions`)

| Field              | Type                       | Default | Required?                                                       |
| ------------------ | -------------------------- | ------- | --------------------------------------------------------------- |
| `ConnectionString` | string                     | —       | **required**                                                    |
| `Embedding`        | `EmbeddingProviderOptions` | —       | **required** (fields below)                                     |
| `Chunking`         | `ChunkingOptions`          | —       | **required** (fields below; LLM is nested under `Chunking.Llm`) |

**`Embedding` / `Llm`** (`ProviderOptions`)

| Field               | Type    | Default            | Required?                                     |
| ------------------- | ------- | ------------------ | --------------------------------------------- |
| `BaseUrl`           | string  | —                  | **required** (enforced by `required` keyword) |
| `Model`             | string  | —                  | **required** (enforced by `required` keyword) |
| `ApiKey`            | string? | `null`             | optional (supports `env:VAR_NAME`)            |
| `RequestsPerMinute` | int?    | `null` (unlimited) | optional                                      |
| `Concurrency`       | int     | `1`                | optional                                      |
| `BatchSize`         | int     | `1`                | optional                                      |

**`Chunking`** (`ChunkingOptions`)

| Field             | Type                  | Default | Required?                                                                                                                    |
| ----------------- | --------------------- | ------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `TargetChunkSize` | int                   | —       | **required** (target size for individual chunks, in chars)                                                                   |
| `ChunkOverlap`    | int                   | —       | **required** (overlap between adjacent chunks, in chars; must be `< TargetChunkSize`)                                        |
| `MaxSegmentChars` | int                   | —       | **required** (hard ceiling on segment size, in chars; must be `>= TargetChunkSize`)                                          |
| `ChunkerType`     | enum                  | —       | **required** (`Custom` or `SemanticKernel`)                                                                                  |
| `Llm`             | `LlmProviderOptions?` | `null`  | optional — when present, enables summarization + contextualization for large documents; when absent, those steps are skipped |

#### How segment sizing works

A "segment" is the largest piece of a document that can be processed as one LLM input. The pipeline segments only when a document is too large to fit; small documents skip segmentation entirely. The per-segment char ceiling is `MaxSegmentChars` — tune it down if the configured LLM rejects inputs as too long.

## Internal layout

| Folder           | Responsibility                                                                                          |
| ---------------- | ------------------------------------------------------------------------------------------------------- |
| `Collections/`   | `CollectionManager`, `Collection` entity                                                                |
| `Configuration/` | `MinervaOptions`, `CredentialResolver`                                                                  |
| `DI/`            | `AddMinerva()` extension, `MinervaStartupService`                                                       |
| `Exceptions/`    | Typed exceptions (`ConfigurationException`, etc.)                                                       |
| `Ingestion/`     | `IngestionPipeline`, `DocumentChunker`, `EmbeddingService`, `DocumentSummarizer`, `ChunkContextualizer` |
| `Models/`        | Public records (`Document`, `SearchResult`, …)                                                          |
| `Providers/`     | OpenAI-compatible embedding/LLM clients, `RateLimiter`, `ProviderFactory`                               |
| `Search/`        | `VectorSearch`, `FullTextSearch`, `ContextExpander`, `SearchPipeline` (RRF fusion)                      |
| `Storage/`       | `SchemaInitializer`, `PostgresCollectionRepository`, `PostgresChunkRepository`                          |
| `Utilities/`     | Cross-cutting helpers                                                                                   |
