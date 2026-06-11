# Minerva — Architecture (core library PRD)

The north-star design for Minerva: a C# / .NET core library providing a
unified embedding, storage, and retrieval engine. The library is
client-agnostic; the first client is a filesystem watcher for Obsidian-style
vaults. Distilled from the 2026-04-04 PRD.

## Goal and shape

A core library that clients (watchers, MCP servers, extractors) call
directly, eliminating Minerva v1's two-step extract → JSON → embed pipeline.
The core API is small:

```csharp
await minerva.IngestAsync(collection, sourceId, title, text, metadata,
    attachments: optionalAttachmentDictionary);   // idempotent by sourceId
await minerva.RemoveAsync(collection, sourceId);
var results = await minerva.SearchAsync(query, collections, options);
```

## Constraints and non-goals

- Clean break from Minerva v1 — new repository, no migration of the Python
  codebase. The product name is **Minerva** (no version suffix); the repo is
  `minerva2` only to distinguish it from the Python v1 repo. Verticals follow
  `Minerva.<Vertical>` (e.g. `Minerva.MarkdownWatcher`).
- Core library is **client-agnostic** — it knows nothing about Obsidian,
  Claude, conversation formats, or any specific source.
- Core library is **text-only** — clients convert media to text before
  ingestion (describe-then-embed).
- **No BGE-M3 dependency** — uses standard OpenAI-compatible endpoints so the
  user can pick any model server.
- **No intermediate JSON file format** between extraction and embedding.
- MCP server design and Claude conversation archival are client concerns, out
  of scope for the core.
- Reranking (cross-encoder second pass) and retrieval-time context enrichment
  (sliding window) are deferred — added later as retrieval-time options.

## Key decisions

- **PostgreSQL + pgvector as the single store** for vectors (HNSW),
  full-text search (tsvector/tsquery), metadata (JSONB + GIN), and reference
  storage. One general-purpose, local-first database; backs up with
  `pg_dump`. Chosen over Qdrant (which adds cloud/Docker friction for a
  personal tool) and over a separate search engine.
- **Metadata as JSONB**, GIN-indexed. Client-agnostic: each client stores
  arbitrary key-value metadata without schema changes.
- **Any OpenAI-compatible model server** — `/v1/embeddings` and
  `/v1/chat/completions` (Ollama, LM Studio, vLLM, OpenAI, …). No lock-in.
- **Collection bound to one embedding model** at creation. Changing the
  embedding model — even to a same-dimension model — requires a full
  re-index, because vector spaces are not interchangeable.
- **Multimodal via describe-then-embed.** Clients pass an *attachment
  dictionary* keyed by the attachment's original syntax in the document text
  (`![](...)`, `![[...]]`, …), valued with at least a text description. The
  core decides how to integrate descriptions; the original document text is
  preserved, enabling future re-processing with a better vision model.
- **Hybrid search with rank fusion** — dense vector similarity + PostgreSQL
  full-text search combined via Reciprocal Rank Fusion. See
  [rank-fusion.md](rank-fusion.md) and [full-text-search.md](full-text-search.md).
- **Contextual preprocessing, per the Anthropic article, optional per
  collection.** (1) summarize the document once; (2) per chunk, send
  summary + chunk to an LLM for a short contextual prefix; (3) prepend the
  prefix before embedding. Optional because local-model ingestion is slow and
  prompt caching is unavailable locally. Large documents are split into
  **segments** at high-level headings (configurable token threshold), each
  segment summarized separately; segments (coarse, for summarization) are a
  different level than chunks (fine, for embedding).
- **Full-text index excludes the contextual prefix** — the tsvector is built
  from chunk text after attachment integration but without the prefix. The
  prefix improves semantic search, not keyword matching; excluding it keeps
  keyword behavior identical whether contextualization is on or off.
- **Multiple collections, per-collection provider config** — embedding model,
  LLM config, and rate limiting per collection.
- **Rate limiting and batching per provider** — requests-per-minute,
  concurrency (critical for local models that handle one request at a time),
  embedding batch size. Carried forward from v1 (`SemaphoreSlim` + sliding
  window token bucket).
- **Client-owned document identity** — clients provide a `sourceId`; ingesting
  an existing `sourceId` replaces all chunks for that document.
- **Atomic updates with retry.** All processing (summarize, chunk,
  contextualize, embed) completes before any DB write; then a single
  transaction deletes old chunks and inserts new ones. A failure partway
  leaves the previous version untouched.

## Ingestion pipeline

```
Document (any size) + optional attachment dictionary
  │
  ├─[1] Integrate attachment descriptions into text
  ├─[2] Summarize document (1 LLM call/doc, or per segment for large docs)   (optional)
  ├─[3] Chunk (markdown-aware; within each segment for large docs)
  ├─[4] Contextualize each chunk (1 LLM call/chunk: summary + chunk → prefix) (optional, paired with [2])
  ├─[5] Embed each contextualized chunk (1 embedding call/chunk)
  └─[6] Atomic store: delete old chunks for sourceId, insert new
        (dense vector + tsvector on post-attachment text + JSONB metadata), one transaction
```

## Research findings behind the design

- **Anthropic Contextual Retrieval** — prepending a short LLM-generated prefix
  that situates each chunk reduces retrieval failure by up to 67% when
  combined with BM25 + embeddings + reranking (35% from contextualization
  alone). Prompt caching brings contextualization cost to ~$1.02 per million
  document tokens, but only on cloud providers, not local servers.
- **BGE-M3 evaluated and rejected** — it gives dense + sparse + ColBERT
  vectors in one pass, but requires a specialized server (not OpenAI-compatible)
  to expose sparse output and would lock the user into one model. PostgreSQL
  full-text search provides the keyword leg without constraining model choice.
  See [retrieval-two-pass-bge.md](retrieval-two-pass-bge.md).
- **v1 rate limiting** — `threading.Semaphore` for concurrency + sliding-window
  token bucket for RPM; embedding batch sizes per provider (OpenAI 50,
  Gemini 20, Ollama 1, LM Studio 1). Carried forward with async equivalents.
