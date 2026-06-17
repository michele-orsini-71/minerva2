# Minerva — C# RAG & AI Memory Core Library - Master Plan

**Status**: All phases complete (35/35, 100%)
**Created**: 2026-04-07
**Last Updated**: 2026-04-18

---

## Executive Summary

Minerva is a C# .NET core library providing a unified embedding, storage, and retrieval engine backed by PostgreSQL + pgvector. It replaces the Python Minerva v1's two-step extract→JSON→embed pipeline with a clean async API (`IngestAsync`, `RemoveAsync`, `SearchAsync`), supporting hybrid search (dense vectors + full-text with rank fusion), optional contextual preprocessing per the Anthropic contextual retrieval article, and multiple collections with per-collection provider config.

The library is client-agnostic — it does not know about Obsidian, Claude, or any specific data source. The first client is a filesystem watcher (`Minerva.MarkdownWatcher`) for Obsidian vaults. Future clients include an MCP server (`Minerva.Mcp`) and a Claude conversation archiver.

**Reference**: The original Python Minerva v1 codebase (separate repo) provides battle-tested patterns for rate limiting, chunking, embedding batching, and incremental updates.

---

## Research Findings

### Codebase Patterns

- **Deterministic IDs**: v1 uses SHA-1/SHA-256 for note/chunk IDs (`minerva/indexing/chunking.py:20-36`). Minerva 2 simplifies: clients provide `sourceId`, chunk IDs derived from `sourceId + chunkIndex + contentHash` via SHA-256.
- **Content-hash change detection**: SHA-256 of document content stored on first chunk, enables O(1) incremental update detection (`minerva/indexing/chunking.py:34-36`, `minerva/indexing/updater.py:247-297`).
- **Rate limiting**: Sliding-window token-bucket with semaphore for concurrency (`minerva/common/ai_provider.py:35-83`). Port to `SemaphoreSlim` + `System.Threading.RateLimiting`.
- **Batch-then-individual fallback**: Attempt full embedding batch; on failure, retry each item individually with exponential backoff (`minerva/indexing/embeddings.py:175-245`). Port to Polly v8.
- **Header-first chunking**: Markdown headers as primary split, recursive character split as secondary (`minerva/indexing/chunking.py:39-106`).
- **L2 normalization**: All embeddings normalized before storage and after retrieval (`minerva/common/ai_provider.py:19-22`).
- **Exception hierarchy**: Single root `MinervaError` with typed subclasses for each subsystem (`minerva/common/exceptions.py`).
- **API key security**: `${ENV_VAR}` template pattern; refuse to store actual secrets (`minerva/common/ai_config.py:23-45`).
- **Collection self-description**: Each collection stores its provider config, embedding model, dimensions as metadata (`minerva/indexing/storage.py:151-212`).

### Dependencies

| Dependency | Purpose |
| ------------ | --------- |
| `Npgsql` (v8+) | PostgreSQL ADO.NET driver |
| `Pgvector` | pgvector type support for Npgsql |
| `OpenAI` SDK (v2.10+) | OpenAI-compatible embedding + chat completions |
| `Polly` v8 | Retry with exponential backoff + jitter |
| `Microsoft.Extensions.AI` | Provider-neutral `IChatClient` / `IEmbeddingGenerator` abstractions |
| `Microsoft.SemanticKernel.Text` | `TextChunker` for markdown/plain-text splitting |
| `Markdig` | Markdown header parsing for chunking |
| `Microsoft.Extensions.Hosting` | `IHostedService` for watcher and startup tasks |
| `Microsoft.Extensions.Options` | Strongly-typed configuration |
| `xUnit` | Unit and integration testing |

### Technical Decisions

| Decision | Rationale | Alternatives Considered |
| ---------- | ----------- | ------------------------ |
| PostgreSQL + pgvector | Single DB for vectors, full-text, metadata. Native .NET support via Npgsql. Local-first. | Qdrant (rejected: Docker friction), ChromaDB (v1, limited) |
| Raw Npgsql, no EF Core | Maximum control over pgvector queries, HNSW index DDL, hybrid search SQL | EF Core (rejected: too much abstraction over pgvector) |
| `Microsoft.Extensions.AI` as provider contract | Standard interfaces, already implemented by OllamaSharp and OpenAI SDK | Custom interfaces (deferred: can refactor later if needed) |
| Client-provided `sourceId` | Simpler than hash-based note IDs. Client knows its own identity. | SHA-1 hash of title+date (v1 pattern, rejected for v2) |
| Monorepo: solution at root, projects as children | Single solution for library + all clients. Simplifies cross-project references. | Separate repos per client (rejected: too much friction) |
| Adjacent chunks as FK columns | O(1) indexed JOINs in PostgreSQL vs colon-delimited string parsing | Metadata string (v1 pattern, worked around ChromaDB limitations) |
| Hybrid search via SQL | Dense vector + tsvector/tsquery combined in one query with rank fusion | Separate search engine (unnecessary with PostgreSQL) |
| Optional contextual preprocessing | 35% retrieval improvement per Anthropic article, but expensive with local models | Always-on (rejected: too slow for bulk imports) |

### Constraints

- **Reuse**: Port v1's rate limiting algorithm, chunking strategy, content-hash change detection, L2 normalization, and API key security pattern.
- **Patterns to follow**: Pragmatic Clean Architecture. Async throughout. `record` types for immutable domain models. `ILogger<T>` for structured logging (never write to stdout from the library).
- **Avoid**: EF Core. Storing actual API keys. Synchronous HTTP calls. Global mutable state. Flat exception types. Full re-index when content hasn't changed.

---

## Architecture Decision

**Approach**: Layered library with pipeline architecture

The core library (`Minerva`) exposes three public methods: `IngestAsync`, `RemoveAsync`, `SearchAsync`. Internally, ingestion and search are implemented as pipelines — ordered sequences of steps that can be individually tested and optionally skipped (e.g., contextual preprocessing).

**Data Flow — Ingestion**:
```text
Document (text + optional attachments + metadata)
    │
    ▼
[1] Integrate attachment descriptions into text
    │
    ▼
[2] Summarize document (optional, 1 LLM call per doc/segment)
    │
    ▼
[3] Chunk (header-first markdown split, then recursive char split)
    │
    ▼
[4] Contextualize each chunk (optional, 1 LLM call per chunk)
    │
    ▼
[5] Embed each chunk (batched, with retry + fallback)
    │
    ▼
[6] Atomic store: DELETE old chunks + INSERT new chunks in single transaction
```

**Data Flow — Search**:
```text
Query text + collection(s) + options
    │
    ▼
[1] Embed query
    │
    ├──────────────────────┐
    ▼                      ▼
[2a] Vector search    [2b] Full-text search
    │                      │
    └──────┬───────────────┘
           ▼
[3] Reciprocal Rank Fusion
    │
    ▼
[4] Context expansion (optional, fetch adjacent chunks)
    │
    ▼
[5] Return SearchResult[]
```

---

## Sub-PRD Overview

| Sub-PRD | Title | Dependency | Status | Document |
| --------- | ------- | ------------ | -------- | ---------- |
| **1** | Solution Scaffold & Foundation | None | ✅ Complete | [01-sub-prd-scaffold.md](./01-sub-prd-scaffold.md) |
| **2** | Storage Layer | 1 | ✅ Complete | [02-sub-prd-storage.md](./02-sub-prd-storage.md) |
| **3** | Provider Layer | 1 | ✅ Complete | [03-sub-prd-providers.md](./03-sub-prd-providers.md) |
| **4** | Ingestion Pipeline | 2, 3 | ✅ Complete | [04-sub-prd-ingestion.md](./04-sub-prd-ingestion.md) |
| **5** | Search Pipeline | 2, 3 | ✅ Complete | [05-sub-prd-search.md](./05-sub-prd-search.md) |
| **6** | Public API & DI | 4, 5 | ✅ Complete | [06-sub-prd-api-di.md](./06-sub-prd-api-di.md) |
| **7** | Markdown Watcher Client | 6 | ✅ Complete | [07-sub-prd-watcher.md](./07-sub-prd-watcher.md) |

**Parallelism**: Sub-PRDs 2 and 3 can be built in parallel. Sub-PRDs 4 and 5 can also be built in parallel.

---

## Implementation Order

### Phase 1: Solution Scaffold & Foundation

**Goal**: Compilable solution with all projects, NuGet refs, exceptions, models, and configuration.

1. ✅ Create `Minerva.sln` and all projects with correct references
2. ✅ Add NuGet packages to each project
3. ✅ Create exception hierarchy (`MinervaException` base + typed subclasses)
4. ✅ Create model records (`Collection`, `Document`, `Chunk`, `SearchResult`, `IngestionResult`, `SearchOptions`, `AttachmentDescription`)
5. ✅ Create `MinervaOptions` and `CredentialResolver`
6. ✅ Create `HashHelper` for deterministic chunk IDs and content hashes

**Verification**:
- [ ] `dotnet build Minerva.sln` compiles with zero errors
- [ ] Run: `dotnet test tests/Minerva.Tests`

⏸️ **GATE**: Phase complete. Continue or `/dev-checkpoint`.

### Phase 2: Storage Layer

**Goal**: PostgreSQL schema, migrations, and repository implementations.

1. ✅ Create `SchemaInitializer` with embedded SQL migration runner
2. ✅ Create SQL migrations (collections, documents, chunks tables; HNSW, GIN, B-tree indexes)
3. ✅ Create `ICollectionRepository` and `IChunkRepository` interfaces
4. ✅ Implement `PostgresCollectionRepository`
5. ✅ Implement `PostgresChunkRepository` with atomic upsert and adjacency FK columns

**Verification**:
- [ ] Schema initializer is idempotent (run twice, no errors)
- [ ] Chunk round-trip integration test passes
- [ ] Run: `dotnet test tests/Minerva.IntegrationTests --filter Category=Storage`

⏸️ **GATE**: Phase complete. Continue or `/dev-checkpoint`.

### Phase 3: Provider Layer

**Goal**: Rate-limited, retry-backed embedding and LLM providers.

1. ✅ Create `RateLimiter` (SemaphoreSlim + sliding-window token bucket)
2. ✅ Create `OpenAICompatibleEmbeddingProvider` implementing `IEmbeddingGenerator` with L2 normalization and Polly retry
3. ✅ Create `OpenAICompatibleLlmProvider` implementing `IChatClient` with rate limiting and Polly retry
4. ✅ Create `ProviderFactory` to construct providers from `MinervaOptions`

**Verification**:
- [ ] Unit tests pass for L2 normalization and retry logic
- [ ] Run: `dotnet test tests/Minerva.Tests --filter Category=Providers`

⏸️ **GATE**: Phase complete. Continue or `/dev-checkpoint`.

### Phase 4: Ingestion Pipeline

**Goal**: Full document ingestion path — chunking, optional contextualization, embedding, atomic storage.

1. ✅ Create `DocumentChunker` (header-first markdown split + recursive char split)
2. ✅ Create `EmbeddingService` (batch + individual fallback)
3. ✅ Create `DocumentSummarizer` (optional LLM summarization)
4. ✅ Create `ChunkContextualizer` (optional per-chunk LLM context prefix)
5. ✅ Create `IngestionPipeline` orchestrator with three-way diff and atomic store

**Verification**:
- [ ] Chunker splits multi-header markdown correctly
- [ ] Pipeline returns correct `IngestionResult` counts with mocked providers
- [ ] Run: `dotnet test tests/Minerva.Tests --filter Category=Ingestion`

⏸️ **GATE**: Phase complete. Continue or `/dev-checkpoint`.

### Phase 5: Search Pipeline

**Goal**: Hybrid search with rank fusion and context expansion.

1. ✅ Create `VectorSearch` (pgvector cosine distance query)
2. ✅ Create `FullTextSearch` (PostgreSQL ts_rank query)
3. ✅ Create `RankFusion` (Reciprocal Rank Fusion)
4. ✅ Create `ContextExpander` (adjacent chunk fetching via FK columns)
5. ✅ Create `SearchPipeline` orchestrator

**Verification**:
- [ ] RRF unit test produces expected merged rank order
- [ ] Run: `dotnet test tests/Minerva.Tests --filter Category=Search`

⏸️ **GATE**: Phase complete. Continue or `/dev-checkpoint`.

### Phase 6: Public API & DI

**Goal**: `MinervaEngine` facade, collection management, DI wiring, end-to-end test.

1. ✅ Create `CollectionManager` (collection CRUD facade)
2. ✅ Create `MinervaEngine` (`IngestAsync`, `RemoveAsync`, `SearchAsync`)
3. ✅ Create `ServiceCollectionExtensions` (`AddMinerva()`)
4. ✅ Create `MinervaStartupService` (`IHostedService` for schema init)
5. ✅ Write end-to-end integration test (ingest → search → remove → verify)

**Verification**:
- [ ] `MinervaEngine` resolves from DI without errors
- [ ] E2E integration test passes against real PostgreSQL
- [ ] Run: `dotnet test tests/Minerva.IntegrationTests --filter Category=E2E`

⏸️ **GATE**: Phase complete. Continue or `/dev-checkpoint`.

### Phase 7: Markdown Watcher Client

**Goal**: Generic filesystem watcher that syncs markdown files (Obsidian vaults, repo docs, static-site sources) into a Minerva collection.

1. ✅ Create `WatcherOptions` (root path, collection name, debounce interval, file glob, excluded directories)
2. ✅ Create `MarkdownScanner` (enumerate .md files, parse YAML frontmatter, derive sourceId)
3. ✅ Create `MarkdownSyncService` (`IHostedService` with `FileSystemWatcher`, debouncing, incremental ingest)
4. ✅ Create `Program.cs` (HostBuilder wiring)
5. ✅ Create `AddMinervaWatcher()` DI extension

**Verification**:
- [ ] `dotnet run` in `src/Minerva.MarkdownWatcher` starts without error
- [ ] Dropping a .md file into a test vault triggers ingestion
- [ ] Run: `dotnet build src/Minerva.MarkdownWatcher`

⏸️ **GATE**: Phase complete. `/dev-checkpoint`.

---

## File Changes Summary

### New Files

| File | Purpose |
| ------ | --------- |
| `Minerva.sln` | Solution file at repo root |
| `Directory.Build.props` | Shared MSBuild properties |
| `Directory.Packages.props` | Central NuGet package version management |
| `.gitignore` | Standard .NET gitignore |
| `global.json` | Pin .NET SDK version |
| `src/Minerva/Minerva.csproj` | Core library project |
| `src/Minerva/MinervaEngine.cs` | Public facade: IngestAsync, RemoveAsync, SearchAsync |
| `src/Minerva/Exceptions/MinervaException.cs` | Base exception with typed subclasses |
| `src/Minerva/Models/Collection.cs` | Collection record |
| `src/Minerva/Models/Document.cs` | Document record |
| `src/Minerva/Models/Chunk.cs` | Chunk record with prev/next IDs |
| `src/Minerva/Models/SearchResult.cs` | Search result with score + context |
| `src/Minerva/Models/IngestionResult.cs` | Added/updated/deleted/unchanged counts |
| `src/Minerva/Models/SearchOptions.cs` | Hybrid alpha, top-k, expand context flag |
| `src/Minerva/Models/AttachmentDescription.cs` | Attachment metadata for ingestion |
| `src/Minerva/Configuration/MinervaOptions.cs` | Strongly-typed options |
| `src/Minerva/Configuration/CredentialResolver.cs` | `${ENV_VAR}` substitution |
| `src/Minerva/Utilities/HashHelper.cs` | Deterministic chunk ID + content hash |
| `src/Minerva/Storage/ICollectionRepository.cs` | Collection persistence interface |
| `src/Minerva/Storage/IChunkRepository.cs` | Chunk persistence interface |
| `src/Minerva/Storage/SchemaInitializer.cs` | Idempotent SQL migration runner |
| `src/Minerva/Storage/Migrations/001_initial.sql` | Collections, documents, chunks tables |
| `src/Minerva/Storage/Migrations/002_indexes.sql` | HNSW, GIN, B-tree indexes |
| `src/Minerva/Storage/PostgresCollectionRepository.cs` | Npgsql-backed collection CRUD |
| `src/Minerva/Storage/PostgresChunkRepository.cs` | Npgsql-backed chunk upsert + adjacency |
| `src/Minerva/Providers/RateLimiter.cs` | Sliding-window token bucket |
| `src/Minerva/Providers/OpenAICompatibleEmbeddingProvider.cs` | IEmbeddingGenerator with L2 norm + Polly |
| `src/Minerva/Providers/OpenAICompatibleLlmProvider.cs` | IChatClient with rate limit + Polly |
| `src/Minerva/Providers/ProviderFactory.cs` | Constructs providers from options |
| `src/Minerva/Ingestion/DocumentChunker.cs` | Header-first markdown chunking |
| `src/Minerva/Ingestion/EmbeddingService.cs` | Batch + individual fallback embedding |
| `src/Minerva/Ingestion/DocumentSummarizer.cs` | Optional LLM summarization |
| `src/Minerva/Ingestion/ChunkContextualizer.cs` | Optional per-chunk LLM context prefix |
| `src/Minerva/Ingestion/IngestionPipeline.cs` | Full ingestion orchestrator |
| `src/Minerva/Search/VectorSearch.cs` | pgvector cosine distance query |
| `src/Minerva/Search/FullTextSearch.cs` | PostgreSQL ts_rank query |
| `src/Minerva/Search/RankFusion.cs` | Reciprocal Rank Fusion merge |
| `src/Minerva/Search/ContextExpander.cs` | Adjacent chunk fetching |
| `src/Minerva/Search/SearchPipeline.cs` | Search orchestrator |
| `src/Minerva/Collections/CollectionManager.cs` | Collection CRUD facade |
| `src/Minerva/DI/ServiceCollectionExtensions.cs` | AddMinerva() extension method |
| `src/Minerva/DI/MinervaStartupService.cs` | IHostedService for schema init |
| `src/Minerva.MarkdownWatcher/Minerva.MarkdownWatcher.csproj` | Watcher client project |
| `src/Minerva.MarkdownWatcher/WatcherOptions.cs` | Watcher configuration |
| `src/Minerva.MarkdownWatcher/MarkdownScanner.cs` | Markdown file enumeration + frontmatter parsing |
| `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs` | IHostedService file watcher with debouncing |
| `src/Minerva.MarkdownWatcher/Program.cs` | Host builder entry point |
| `src/Minerva.MarkdownWatcher/DI/ServiceCollectionExtensions.cs` | AddMinervaWatcher() extension |
| `tests/Minerva.Tests/Minerva.Tests.csproj` | Unit test project |
| `tests/Minerva.IntegrationTests/Minerva.IntegrationTests.csproj` | Integration test project |

### Modified Files

None — greenfield project.

---

## Reference Files

- `../minerva/minerva/common/ai_provider.py` — Rate limiter, L2 normalization, embedding generation
- `../minerva/minerva/indexing/chunking.py` — Chunking pipeline, ID generation, content hashing
- `../minerva/minerva/indexing/embeddings.py` — Batch processing, retry, progress callbacks
- `../minerva/minerva/indexing/updater.py` — Incremental update algorithm (three-way diff)
- `../minerva/minerva/indexing/storage.py` — Adjacent chunk computation, collection metadata
- `../minerva/minerva/common/exceptions.py` — Exception hierarchy design
- `../minerva/minerva/common/ai_config.py` — API key security pattern, provider config
- `../minerva/minerva/server/context_retrieval.py` — Context expansion algorithm
