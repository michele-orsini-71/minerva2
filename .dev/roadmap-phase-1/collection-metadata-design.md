---
slug: collection-metadata-design
title: Collection Metadata & Provenance — Design
status: design
parent: phase-1-progress.md
---

# Collection Metadata & Provenance — Design

A pre-1D fixed point. Before the Wikipedia ingest we make every collection
self-describing and drift-proof, so the test collections and the new corpus are
re-ingested once into fully documented collections, with no retrofit onto live
data later.

This document is high level on *how* the work is cut, but exhaustive on *what*
the `collections.metadata` column will contain. It is written so that the actor
writing the code is interchangeable: each phase is an independent, verifiable
slice that leaves the build and tests green.

## Why

1. **Self-describing collections.** Today a collection records only its
   embedding model name. Nothing says how its chunks were built.
2. **Drift-proofing.** Ingest is a full reconcile —
   [MinervaIngestEngine.cs:62-67](../../src/Minerva/MinervaIngestEngine.cs#L62-L67)
   deletes every source the current run did not produce. Re-ingesting the same
   collection with a changed chunk size (or model, or prompt) silently produces
   an incoherent index. Only the embedder is guarded today; nothing else is.
3. **Tier-2 provenance in the bench.** `run.json` should record how the
   collection under test was built, not just the bench version.

## Principles

- **Two owners.** Core provenance is typed, interpreted, and guarded by Minerva
  core. Client metadata is opaque to core — stored verbatim, owned by the
  front-end that produced the collection (the markdown indexer today; an MCP
  server or Obsidian plugin tomorrow). Core never interprets the client section,
  so core stays uncoupled from front-end concepts.
- **Three classes of field.** *Invariant* — defines the stored bytes; changing
  it mid-collection is incoherent, so it is guarded (hard error on drift).
  *Last-run* — recorded for the record, refreshed each ingest, never guarded.
  *Excluded* — secrets, infrastructure, and runtime throughput knobs are never
  stored.
- **Versioning a prompt promotes it from code to invariant.** A hardcoded prompt
  is a content determinant the guard cannot see. Giving it an explicit version
  constant makes it a guardable invariant. Other code-level determinants
  (embedding input composition, FTS config, chunker algorithm, attachment
  integration) are *not* individually versioned; they are pinned by the recorded
  ingestor git SHA. To reproduce a collection exactly, check out that SHA.

## The metadata column — full contents

`collections.metadata JSONB` becomes a two-section object. `created_at` and
`last_updated_at` stay as their own columns (unchanged). The `embedding_model`
and `embedding_dimension` columns are dropped and fold into `provenance`.

```json
{
  "provenance": {
    "invariants": {
      "embeddingModel": "bge-m3",
      "embeddingDimension": 1024,
      "chunkerType": "Custom",
      "targetChunkSize": 512,
      "chunkOverlap": 64,
      "maxSegmentChars": 8000,
      "contextualizationEnabled": true,
      "contextualizationModel": "qwen2.5",
      "summarizerPromptVersion": "1",
      "contextualizerPromptVersion": "1"
    },
    "lastRun": {
      "ingestorVersion": "0.1.0+9d1eab5",
      "schemaVersion": "003_collection_provenance"
    }
  },
  "client": {
    "kind": "markdown-indexer",
    "data": {
      "sourceRoot": "/home/user/vault"
    }
  }
}
```

> Implemented shape (slice E): the `client` section is a typed
> `ClientProvenance(kind, data)` record, not a flat object. `kind` is a mandatory
> envelope field; everything front-end-specific lives in the opaque `data` map.
> The markdown indexer currently writes only `sourceRoot` into `data`; globs are
> "free" (not stored).

### `provenance.invariants` — guarded, drift is a hard error

| Key | Type | Source | Notes |
| ----- | ------ | -------- | ------- |
| `embeddingModel` | string | `Embedding.Model` | Was the `embedding_model` column. |
| `embeddingDimension` | int | probed via `IEmbeddingDimensionProvider` | Was the `embedding_dimension` column. Catches a model that silently changed dimension. |
| `chunkerType` | enum (`Custom`/`SemanticKernel`) | `Chunking.ChunkerType` | |
| `targetChunkSize` | int | `Chunking.TargetChunkSize` | |
| `chunkOverlap` | int | `Chunking.ChunkOverlap` | |
| `maxSegmentChars` | int | `Chunking.MaxSegmentChars` | Sets the large-document threshold. |
| `contextualizationEnabled` | bool | `Chunking.Llm is not null` | Context-vs-no-context is the whole eval axis. |
| `contextualizationModel` | string? | `Chunking.Llm.Model` | Present only when enabled. |
| `summarizerPromptVersion` | string? | `DocumentSummarizer.PromptVersion` | Present only when enabled. New constant (Phase B). |
| `contextualizerPromptVersion` | string? | `ChunkContextualizer.PromptVersion` | Present only when enabled. New constant (Phase B). |

When `contextualizationEnabled` is `false`, the three contextualization fields
below it are absent. A reingest that flips `enabled`, or changes the model or a
prompt version, is drift.

### `provenance.lastRun` — recorded only, refreshed each ingest

| Key | Type | Source | Notes |
| ----- | ------ | -------- | ------- |
| `ingestorVersion` | string | baked `InformationalVersion` (`semver+sha`) | Same reader the bench and `--version` already use. The pin for all un-versioned code determinants. |
| `schemaVersion` | string | `MAX(name)` from `_migrations` | The migration shape the chunks were written under. |

The last-ingest timestamp is the existing `last_updated_at` column, not a bag
field.

### `client` — opaque to core, owned by the front-end

| Key | Type | Notes |
| ----- | ------ | ------- |
| `kind` | string | Mandatory discriminator (`"markdown-indexer"`, later `"mcp"`, `"obsidian"`). |
| `data` | map | Opaque to core. Front-end fields; for the markdown indexer see the indexer section. |

Core enforces only that `kind` is present (the envelope contract) and never
inspects `data`. A read-time boundary guard in `ReadCollection` rejects a
collection whose `client` (or `provenance`) section is missing, so the
non-nullable model is honest for every downstream reader. The client-envelope
`version` stamp is deferred to `docs/future/backlog.md`.

### Excluded — never stored

Secrets (`ApiKey`), `ConnectionString`, runtime throughput knobs (`Concurrency`,
`BatchSize`, `RequestsPerMinute`, `BaseUrl`), and front-end paths in the *core*
provenance. The existing `ValidateNoLiteralApiKeys` check stays.

## Enforcement model

- **Core invariants — hard lock.** On reingest the engine compares the
  configured invariant set against the stored one and throws
  `CollectionConfigMismatchException` listing every drifted field (name, stored
  value, configured value), unless the caller passes the override flag, in which
  case it drops and recreates. This generalizes today's embedder-only check at
  [MinervaIngestEngine.cs:83-94](../../src/Minerva/MinervaIngestEngine.cs#L83-L94).
- **Client metadata — not guarded by core.** Core cannot interpret it. Any
  guard over client fields lives in the front-end (see the indexer section).

## Phases

Ordered by dependency. Each ends with the build and tests green, so a different
actor can pick up the next one cleanly. A → B → C → D is the core line; E depends
only on A and does not block the Wikipedia ingest.

### Phase A — schema + typed model round-trip

No behaviour change yet; just reshape storage.

- Migration `003_collection_provenance.sql`: drop `embedding_model` and
  `embedding_dimension` columns.
- New `CollectionProvenance` record (`Minerva.Models`) with the `invariants` and
  `lastRun` groups above.
- `Collection` model: `Metadata` becomes the two-section structure;
  `EmbeddingModel` / `EmbeddingDimension` stay as typed properties but hydrate
  from `provenance.invariants`.
- `PostgresCollectionRepository`: (de)serialize the nested bag; keep timestamps
  as columns.
- **Verify:** unit test round-trips a `Collection` with a full provenance object
  through `CreateAsync` / `GetAsync`.

### Phase B — prompt versioning + populate at ingest

- Add a co-located `const string PromptVersion` to `DocumentSummarizer` and
  `ChunkContextualizer`, next to each template, bumped when the text changes.
- The ingest builder assembles the configured invariant set (it has
  `options.Chunking` and the prompt-version constants in scope); the engine adds
  the probed dimension and the `lastRun` fields, and writes provenance on create
  via `EnsureAsync`.
- **Verify:** integration test ingests into a fresh collection, reads it back,
  asserts every populated provenance field.

### Phase C — enforcement (generalized guard)

- Generalize `CollectionEmbedderMismatchException` →
  `CollectionConfigMismatchException` (reports each drifted field).
- Extend `PrepareCollectionAsync` to compare the whole invariant set.
- Rename `AllowRecreateOnEmbedderMismatch` → `AllowRecreateOnConfigMismatch`
  across the option record, binder, `IIngestEngine`, the markdown indexer, and
  the three affected test files.
- **Verify:** integration test — reingest with a changed `targetChunkSize`
  throws; with the override flag it drops and recreates.

### Phase D — bench tier-2 provenance

- `RunJsonWriter` fetches the collection under test and stamps its `provenance`
  block into `run.json`.
- **Verify:** run a sweep; assert `run.json` carries the provenance block.

### Phase E — indexer client metadata + soft path guard

Separable; depends only on Phase A. Does not block 1D.

- The markdown indexer writes its `client` section at ingest (see below).
- On reingest it reads back its own stored `client` and applies its own policy.
- **Verify:** reingest with a changed source root warns/blocks; with the
  override flag it proceeds.

## Indexer part

Core hands the front-end an opaque `client` section and stays out of the policy.
**It is up to the indexer to decide which of its settings is critical, which is
overridable, and which is free.** Proposed default classification for the
markdown indexer (the indexer owns this table, not core):

| Field | Class | Behaviour on change at reingest |
| ------- | ------- | --------------------------------- |
| `sourceRoot` | **Overridable** | Warn and block unless `AllowSourceRootChange`. A wrong root is destructive: the reconcile deletes every source from the old root. Sometimes legitimate (vault reorganised, deliberate second folder), so it is a guarded warning, not a hard lock. |
| `includeGlobs` / `excludeGlobs` | **Free** | Record only. Changing which files match is the indexer's normal job; the reconcile handles additions and deletions as usual. |
| `kind` | **Critical** | A different `kind` means a different front-end owns the collection; refuse. |

The override flag mirrors the existing `AllowRecreate…` pattern — an explicit
opt-in, defaulted off. The distinction from the core invariants is the
enforcement strength: core invariants are always-incoherent hard locks; the
indexer's `sourceRoot` is usually-a-mistake, so it warns and offers an override.
