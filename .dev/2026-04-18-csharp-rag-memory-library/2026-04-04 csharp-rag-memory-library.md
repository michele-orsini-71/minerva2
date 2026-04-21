# Minerva-2: C# RAG & AI Memory Core Library

**Created**: 2026-04-04
**Last Updated**: 2026-04-06

## Goal

Build a C# / .NET core library that provides a unified embedding, storage, and retrieval engine. The library's design is general enough to support both personal knowledge bases (successor to Minerva) and AI agent memory systems (for Claude Code and similar), but the first implementation targets the knowledge base use case. The library exposes a clean async API that clients (extractors, watchers, MCP servers) call directly — eliminating the two-step extract→JSON→embed pipeline from Minerva v1. First client is a filesystem watcher suitable for Obsidian vaults.

## Constraints and Non-Goals

- Not a migration of Minerva — clean break, new repository ("Minerva-2")
- Core library is client-agnostic — it does not know about Obsidian, Claude, conversation formats, or any specific data source
- Core library is text-only — multimodal content (images, audio, etc.) must be converted to text descriptions by the client before ingestion
- No BGE-M3 dependency — uses standard OpenAI-compatible embedding endpoints so the user can choose any model server
- No intermediate JSON file format between extraction and embedding
- MCP server design is out of scope for the core library (it's a client)
- Claude conversation archival/client is out of scope for the core library
- Reranking (cross-encoder second pass) is deferred — can be added later as a retrieval-time optimization
- Retrieval-time context enrichment (sliding window around results) is deferred

## Superseded Documents

The following documents in the Minerva v1 repository were created during earlier planning and are superseded by this PRD:

- `docs/CSHARP_MIGRATION_FEASIBILITY.md` — recommended Qdrant as the vector DB; this PRD chooses PostgreSQL + pgvector instead
- `docs/CSHARP_MIGRATION_PLAN.md` — proposed a solution architecture based on the feasibility study; this PRD defines a different architecture

These documents remain in the repository for historical reference but should not be used as guidance for Minerva-2 implementation.

## Decisions

### New repository, not a migration

**Choice**: Create a new repo "Minerva-2" rather than evolving the existing Python Minerva repo.
**Rationale**: Clean break. Different language, different architecture, different pipeline. No baggage from the Python codebase. Can always rename later.

### Naming convention

**Choice**: The core library/namespace is **Minerva** (no version suffix). The repo is `minerva2` solely to distinguish it from the Python v1 repo. Vertical clients follow the pattern `Minerva.<Vertical>` (e.g., `Minerva.Watcher`, `Minerva.Mcp`).
**Rationale**: The project is a clean break, not a continuation — a version suffix in the product name would imply continuity. The `Minerva.*` namespace hierarchy keeps verticals clearly associated with the core while giving each its own identity.

### C# / .NET

**Choice**: C# with .NET (latest stable) as the implementation language.
**Rationale**: Developer preference — clearer code to read and navigate. Strong typing, excellent tooling, first-class async support, official SDKs for MCP (Anthropic + Microsoft), Ollama (OllamaSharp), and all major AI providers.

### PostgreSQL + pgvector for storage

**Choice**: PostgreSQL with the pgvector extension as the single database for vectors, full-text search, and metadata.
**Rationale**: One database covers all needs — dense vector search (pgvector with HNSW), keyword/BM25 search (built-in tsvector/tsquery), metadata filtering (relational with JSONB), and reference storage. First-class .NET support via Npgsql with native pgvector integration. Good balance between complexity and power. Runs locally, backs up with pg_dump, and is a general-purpose tool useful beyond this project.
**Note**: The earlier feasibility study (docs/CSHARP_MIGRATION_FEASIBILITY.md) recommended Qdrant. PostgreSQL was chosen instead during planning because Qdrant is primarily designed for cloud/Docker deployment, which adds infrastructure friction for a personal local tool. PostgreSQL provides equivalent capabilities in a single, general-purpose database.

### Metadata stored as JSONB

**Choice**: Document and chunk metadata is stored in PostgreSQL JSONB columns, indexed with GIN indexes for efficient filtering.
**Rationale**: The library is client-agnostic, so metadata fields vary per client (Obsidian notes have tags and vault paths; Claude sessions have project names and timestamps). JSONB allows each client to store arbitrary key-value metadata without schema changes, while GIN indexes preserve fast filtering.

### Any OpenAI-compatible model server

**Choice**: The library connects to any server exposing the OpenAI-compatible `/v1/embeddings` and `/v1/chat/completions` endpoints (Ollama, LM Studio, vLLM, LocalAI, OpenAI itself, etc.).
**Rationale**: Keeps the user free to choose any model and any server. No lock-in to a specific embedding model or provider. This was a strength of Minerva v1 worth preserving.

### Collection bound to a specific embedding model

**Choice**: Each collection is bound to a specific embedding model at creation time. Any change to the embedding model — even to a different model with the same vector dimensions — requires re-indexing the entire collection, because different models produce incompatible vector spaces.
**Rationale**: pgvector HNSW indexes are dimension-specific, and even same-dimension models are not interchangeable (their vector spaces are trained differently). This is a natural constraint of embedding technology.

### Multimodal via describe-then-embed (client responsibility)

**Choice**: Clients handle all media→text conversion. The core library only accepts text. Clients pass an attachment dictionary alongside the document text, mapping attachment syntax (as it appears in the source document) to attachment info (description, source path, and any other client-relevant data).
**Rationale**: Keeps the core library text-only, which preserves the "pick any embedding model" design. Different clients can handle attachments differently — or ignore them entirely. The attachment dictionary preserves the original document text untouched while giving the library the information it needs to integrate descriptions during ingestion. This also handles the markdown dialect problem: the client knows its own syntax (`![](...)` vs `![[...]]` vs `[[@...]]`) and resolves it.

### Attachment dictionary pattern

**Choice**: The Ingest API accepts an optional dictionary keyed by the attachment's original syntax in the document text, with values containing at minimum a text description and optionally the source path and any other metadata. The same pattern handles images, inner links, embeds, and any other non-text content.
**Rationale**: The core library can decide how to integrate descriptions (inline replacement, appending, extra context for chunking) without the client needing to manipulate the original text. The original document is preserved, enabling future re-processing (e.g., with a better vision model). Different attachment syntax flavors are handled naturally since the client provides the exact string to match. This could also serve as a starting point for A/B evaluation (with/without attachment descriptions).

### Hybrid search with rank fusion

**Choice**: Dense vector similarity + PostgreSQL full-text search, combined via rank fusion in a single SQL query.
**Rationale**: Implements the Anthropic contextual retrieval article's recommended approach without needing a separate search engine. PostgreSQL's tsvector/tsquery handles the keyword side, pgvector handles the semantic side, and a weighted combination produces the final ranking. No BGE-M3 sparse vectors needed.

### Contextual preprocessing with document summary

**Choice**: Two-step contextual preprocessing per the Anthropic article:
1. Generate a summary of the entire document (one LLM call per document)
2. For each chunk, send summary + chunk to the LLM to generate a short contextual prefix (one LLM call per chunk)
3. Prepend the prefix to the chunk before embedding

Contextual preprocessing is **configurable per collection** and can be disabled — for example, during bulk imports, when using slow local models, or when the latency/cost is not justified.

For large documents exceeding a configurable token threshold (e.g., 8,000 tokens), the document is split into **segments** at natural break points (highest-level markdown headings that keep segments under the threshold), each segment is summarized separately, and each chunk uses the summary of its enclosing segment. If no suitable headings are found, the library falls back to fixed-size token segments with overlap. The threshold is configurable per collection. Note: segments (coarse, for summarization) are a different level than chunks (fine, for embedding). Chunking in step [3] of the pipeline happens independently within each segment.

**Rationale**: The Anthropic article measured 35% improvement in retrieval accuracy from contextual preprocessing alone. Using a document summary (rather than the full document) makes this scalable without hitting context window limits. Segmented summarization handles very large documents (books, long conversations) where a single global summary would lose specificity. Making it optional acknowledges that ingestion with local models can be slow (a 500-note vault with 5 chunks each = 2,500+ LLM calls) and prompt caching (which dramatically reduces cost on cloud providers) is not available on local model servers.

### Full-text search indexes chunk text without contextual prefix

**Choice**: The PostgreSQL tsvector full-text index is built from chunk text **after attachment integration but without the contextual prefix**. The contextual prefix is only used for the dense embedding.
**Rationale**: This keeps keyword search behavior consistent regardless of whether contextual preprocessing is enabled or disabled for a collection. The contextual prefix is designed to improve semantic search (embeddings), not keyword matching. Including attachment descriptions in the keyword index is intentional — the description text is more useful for keyword search than the original attachment syntax (e.g., `![[IMG_001.jpg]]`).

### Multiple collections with per-collection provider config

**Choice**: The library supports multiple named collections (e.g., "obsidian-vault", "claude-sessions"), each with its own embedding model, LLM configuration, and rate limiting settings.
**Rationale**: Carries forward a Minerva v1 strength. Different collections may have different performance/cost/quality requirements. A personal notes collection might use a local Ollama model, while a code documentation collection might use OpenAI's embedding API.

### Rate limiting and batching per provider

**Choice**: The per-collection provider configuration includes rate limiting and concurrency controls (carrying forward Minerva v1's `RateLimitConfig` pattern):
- **Requests per minute** — throttles total API calls for cloud providers with rate limits
- **Concurrency** — limits parallel in-flight requests (critical for local models like Ollama that can only handle 1 concurrent request)
- **Embedding batch size** — provider-specific (e.g., OpenAI supports batch embedding, Ollama processes one at a time)

**Rationale**: Different providers have vastly different capacity. Cloud APIs can handle parallel requests but enforce RPM limits. Local models (Ollama, LM Studio) often crash or become unresponsive under concurrent load. Minerva v1 implemented this pattern successfully with a semaphore for concurrency and a sliding-window token bucket for RPM. The C# implementation will use async equivalents (SemaphoreSlim, etc.).

### Client-owned document identity

**Choice**: Clients provide a `sourceId` for each document. The core library uses this to manage chunks — ingesting with an existing sourceId replaces all chunks for that document.
**Rationale**: The library is client-agnostic. A filesystem watcher might use the file path as sourceId, a Claude client might use the session ID. The library doesn't generate IDs or make assumptions about what a "document" is.

### No intermediate file format

**Choice**: Extractors/clients call the library API directly to ingest content. No JSON files on disk as an intermediate step.
**Rationale**: Minerva v1's two-step pipeline (any format → JSON → embedding) was a design constraint that added friction and complexity. With a library exposing a clean `Ingest` API, any client can feed content directly. The library handles chunking, contextualization, embedding, and storage internally.

### Error handling: atomic updates with retry

**Choice**: The ingestion pipeline uses a retry policy for transient failures (LLM timeouts, embedding service hiccups). Processing (summarization, chunking, contextualization, embedding) happens first, fully, before any database writes. Once all chunks are successfully processed, the database update is atomic: delete old chunks and insert new ones in a single transaction. If processing fails partway through, the previous version of the document remains untouched in the database. The API return clearly reports success or failure with details.
**Rationale**: This avoids the risk of mixed old/new chunks or incomplete documents in the database. If the LLM fails on chunk 15 of 20, the old version stays intact. The client can retry the entire document later. Processing is the expensive part (LLM calls); the database swap is cheap and fast.

### Filesystem watcher as first client

**Choice**: The first client application is a filesystem watcher that monitors a directory (e.g., an Obsidian vault) and ingests new/changed files automatically.
**Rationale**: Provides a concrete test bed for the core library. Immediately useful for the personal knowledge base use case. Exercises the full ingestion pipeline including incremental updates.

## Approach Preferences

- Pragmatic Clean Architecture — apply principles where they add value, no zealotry
- The core library is the product; clients are consumers
- Keep the core API simple: `IngestAsync`, `RemoveAsync`, `SearchAsync`
- Design for local-first usage — minimal infrastructure, no cloud dependencies required
- Everything becomes text before it hits the embedding pipeline

## Open Questions

(none — all resolved during conversation)

## Deferred Decisions

- **MCP server design** — this is a client of the core library; different MCP servers may return different result formats depending on the consuming AI/app. Client concern, not core.
- **Claude conversation client** — how to archive and ingest Claude Code sessions. Client concern.
- **Reranking** — cross-encoder second pass at retrieval time. Can be added later as an optimization to the search pipeline.
- **Retrieval-time context enrichment** — sliding window of surrounding chunks returned with search results. Minerva v1 already does this; will be implemented but design is deferred.

## Implementation-Phase Details

The following items are intentionally left to the implementation phase. They are standard engineering concerns with well-known solutions that do not require design decisions at the PRD level:

- **Database schema** — table structure, column definitions, and index creation. The decisions above (JSONB, pgvector HNSW, GIN indexes, tsvector, per-collection config) are sufficient to derive the schema.
- **Rank fusion algorithm** — Reciprocal Rank Fusion (RRF) is the natural default; specifics like weight configurability are implementation details.
- **API type definitions** — concrete C# types for `AttachmentInfo`, `IngestResult`, `SearchOptions`, `SearchResult`, etc. The behavior and semantics are defined above; the types follow from them.
- **Collection lifecycle API** — whether collections are created implicitly on first ingest or explicitly via a `CreateCollectionAsync` method.
- **Database connection management** — connection pooling (Npgsql built-in), connection string configuration, schema migrations (EF Core or raw SQL). PostgreSQL's MVCC ensures multi-process safety (e.g., watcher + MCP server accessing the same database).
- **Chunking defaults** — chunk size, overlap, and splitting strategy. Minerva v1's patterns (1200-char chunks, markdown-aware splitting) provide a proven starting point.
- **.NET version** — "latest stable" at implementation time. The feasibility study was written against .NET 9; implementation may target .NET 9 or later.
- **RemoveAsync scope** — removes all artifacts for the given sourceId (chunks, metadata). Collection-level removal is part of the collection lifecycle API.

## Research Findings

- **Anthropic Contextual Retrieval**: The article recommends contextualizing chunks by prepending a short LLM-generated prefix that situates the chunk within its document. Combined with BM25 + embeddings + reranking, this achieves 67% reduction in retrieval failure rate. Prompt caching reduces contextualization cost to ~$1.02 per million document tokens (cloud providers only — not available on local model servers). The hybrid search uses parallel retrieval (keyword + semantic) with rank fusion, not sequential filtering.

- **BGE-M3 evaluated and rejected**: BGE-M3 provides dense + sparse + ColBERT vectors in one pass, which would natively support hybrid search. However, it requires a specialized model server (not standard OpenAI-compatible API) to expose sparse outputs, and it would lock the user into one embedding model. Using PostgreSQL's built-in full-text search for the keyword side achieves the same hybrid pattern without constraining model choice.

- **PostgreSQL hybrid search**: pgvector supports HNSW indexing for dense vector search. PostgreSQL's built-in tsvector/tsquery provides mature full-text search with multilingual support. Both can be combined in a single SQL query with weighted rank fusion. Npgsql (.NET driver) has native pgvector support.

- **C# ecosystem readiness**: Confirmed via feasibility assessment — all critical dependencies have mature C# equivalents. Official MCP SDK (Anthropic + Microsoft), OllamaSharp, Npgsql with pgvector, Microsoft.SemanticKernel.Text for chunking. Full details in docs/CSHARP_MIGRATION_FEASIBILITY.md (note: that document recommends Qdrant, which this PRD supersedes with PostgreSQL).

- **Minerva v1 rate limiting**: Implemented with a threading.Semaphore for concurrency control and a sliding-window token bucket for RPM throttling. Embedding calls are batched per provider (OpenAI=50, Gemini=20, Ollama=1, LM Studio=1). This pattern will be carried forward in the C# implementation using async equivalents.

## Ingestion Pipeline

```
Document (any size) + optional attachment dictionary
    │
    ▼
[1] Integrate attachment descriptions into text
    │
    ▼
[2] Summarize document (1 LLM call per document, or per segment for large docs)
    │  (optional — skipped when contextual preprocessing is disabled)
    ▼
[3] Chunk (split into segments, markdown-aware; within each segment for large docs)
    │
    ▼
[4] Contextualize each chunk (1 LLM call per chunk: summary + chunk → short prefix)
    │  (optional — same setting as step [2]; steps [2] and [4] are always skipped together)
    ▼
[5] Embed each contextualized chunk (1 embedding call per chunk)
    │
    ▼
[6] Atomic store in PostgreSQL:
    - Delete old chunks for this sourceId (if updating)
    - Insert new chunks (dense vector + tsvector on post-attachment text + JSONB metadata)
    - All in a single transaction
```

## Core API Shape

```csharp
// Ingest or update a document — idempotent by sourceId
// Processes fully, then atomically swaps in the database
// Returns result with success/failure details
await minerva.IngestAsync(collection, sourceId, title, text, metadata,
    attachments: optionalAttachmentDictionary);

// Remove a document and all its chunks
await minerva.RemoveAsync(collection, sourceId);

// Search across one or more collections
var results = await minerva.SearchAsync(query, collections, options);
```
