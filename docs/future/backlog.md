# Minerva — future backlog (speculative)

A spectrum of possibilities beyond the committed implementation roadmap. These
are **not** planned work; they are ideas, options, and wishes, kept so they are
not lost. The committed plan lives in
`.dev/new-implementation-roadmap/` and `.dev/roadmap-phase-1/`; items already
absorbed there are pointed to rather than repeated.

Translated and distilled from the old Obsidian master note and several
exploratory chats. Low confidence by design — revisit before acting.

## Already on the committed roadmap (do not duplicate here)

- **Reranking** (cross-encoder second pass) → roadmap Phase 2.
- **Chunk-size / overlap tuning** → roadmap Phase 3 (method in
  `docs/measurements/chunk-size-and-prefix.md`).
- **Dedupe alternatives** (cap-per-source, positional, MMR) → roadmap Phase 5.
- **Chunk expansion ±W** → roadmap Phase 4.
- **Real BM25** (Postgres extension) and **per-document language detection** →
  roadmap "Cross-phase backlog".
- **Ingestor versioning / reindex policy** and **`collection_metadata` table**
  → roadmap "Cross-phase backlog".

## Retrieval and answer quality

- **Query understanding / expansion** — rewrite or expand the query before
  retrieval (HyDE-style hypothetical answer, sub-question decomposition).
- **Answer generation with citations** — close the loop from retrieval to a
  grounded answer; relates to the eval harness's deferred LLM-as-judge slice.
- **Observability** — per-stage logging and metrics as a first-class surface,
  beyond the eval harness's offline `details.jsonl`.
- **Metadata-filter search** — filter retrieval by metadata (paths, folders,
  date, tags); lets a client exclude an archive folder, or scope to a subtree.
  Noted as valuable in *AI Engineering* and the Anthropic article (entities and
  keywords as chunk metadata, e.g. exact error codes).
- **Document retriever mode** — given the index, return the note (or list of
  notes) that discuss a topic, as an alternative to a summarized answer. A
  "search that returns sources", useful for tools like Obsidian.
- **Return raw search results to the client** — instead of always handing the
  result to an LLM for summarization, optionally return the ranked hits for the
  client to render, with client-side filtering (exclude paths, include/exclude
  archive).
- **Summarizer / contextualizer quality** — the Anthropic recipe passes the
  whole document with each chunk; Minerva uses a document summary for time and
  cost. Could improve by constraining the summary to a size relative to the
  source, or a fixed budget.

## Ingestion features

- **Attachment / image extraction** — notes with images: extract a description
  and integrate it (the attachment-dictionary pattern already designed in the
  PRD).
- **YAML frontmatter variants** — handle none / malformed (missing close) /
  quoted / array / bool values robustly in the parser.
- **Chunking beyond markdown** — current chunker is markdown-first but can take
  plain text; consider giving blank lines priority (not only line breaks),
  Q&A-style chunking, and non-English handling.
- **Prompt caching for non-local models** — explore whether the Python / MS
  SDKs expose prompt caching for cloud providers, or whether a hand-rolled
  driver is needed, to cut contextualization cost.

## Clients and feature parity

- **A first non-watcher client** — an MCP server or a CLI/GUI tool to exercise
  and inspect `SearchAsync`. For small documents, returning the whole document
  rather than chunks is a useful variant.
- **Feature parity with the original (Python) Minerva** — collect the v1
  commands (`index`, `serve`, `serve-http`, `peek`, `remove`, `validate`,
  `query`, `keychain`; and `minerva-kb`: `add/list/status/sync/watch/remove/serve`)
  and decide which still make sense for v2. Many (`peek`, `remove`) become thin
  wrappers; `validate` is obsolete. Open question: unify into a
  `MinervaServices` facade vs keep thin, unopinionated wrappers (it is open
  source — a user can fork).
- **Watcher orchestration GUI** — a tool to fire, configure, and monitor
  multiple watchers; deferred. Preflight is already a library capability any
  client can reuse.
- **Cross-module versioning** — keep the same version across modules; decide a
  policy.
- **Auto-rebuild on embedder change** — when a configured embedder produces a
  different vector dimension than an existing collection, the check fails with
  clear remediation and the user decides (drop + rebuild, or revert). Automatic
  rebuild is deferred.

## Obsidian support (potential plugin)

The generic markdown watcher stays pure; Obsidian specifics become a future
`Minerva.Obsidian` plug-in with custom preprocessors. Deferred Obsidian /
markdown features: image vision-model descriptions, OCR, internal-link
resolution, code-block/table atomicity during chunking, footnotes, wikilinks,
embeds, tags, aliases, dataview queries, callouts. A plug-in could also be a
route to wider adoption.

## Multimodal embeddings — low hope for now

CLIP is multimodal (text + image encoders) but **LM Studio / Ollama cannot
serve it as an embedding endpoint**. On Apple Silicon, multimodal embeddings
require a **sidecar Python service** (sentence-transformers, open_clip, MLX,
Nomic Embed Vision, or Jina CLIP), not LM Studio. Treat as "no clean path yet";
revisit only if multimodal becomes a priority.

## Model comparison & performance report (after the reranker)

**Current decision:** `qwen2.5` is the standing contextualization model;
`gemma4` is shelved — ruled out for bulk ingest on cost (~5× qwen, ~300×
no-context). Because the eval measures *relative* deltas with the
contextualizer held fixed, the absolute model choice does not block
development. Recorded in the roadmap "On resume" fixed points.

**Future task** — to run once the implementation is on solid bases (after
Phase 2, the reranker, and possibly after further development): reconsider the
contextualization (and embedding) model properly, and produce a publishable
performance report.

- Research other **local** candidates (e.g. Gemma 2 2B Q4_K_M, Qwen 2.5 3B
  Instruct, Llama 3.2 3B) and include some **online / API** models for
  contrast.
- Ingest the corpus under each candidate into separate collections.
- Run the bench across them and produce a **status report of app retrieval
  performance** — suitable to publish (e.g. in the README).
- Judge gemma4's viability together with the serving-speed levers in
  `docs/measurements/model-speedups.md` (speculative decoding, MLX).

This is a cross-collection comparison (re-ingest per model), so it is
deliberately deferred — not on the critical path for tuning the pipeline. The
one in-band absolute check worth running earlier is context vs no-context
(is the contextualization step worth its cost), on the already-indexed
personal-notes collections.

## "What makes a good RAG" — priorities

From a review of the system against good-RAG practice. Minerva already has:
hybrid retrieval, a multilingual embedder (bge-m3), contextual prefixes,
structure-aware chunking, dedupe. The gaps, in priority order:

1. **Evaluation harness** — the most important; now in progress (Phase 1).
2. **Reranking** — Phase 2.
3. **Query understanding / expansion**.
4. **Answer generation with citations**.
5. **Observability**.
6. **Per-collection embedding-model fingerprint** — overlaps with the
   `collection_metadata` roadmap item.

The guiding point: good RAG is not about the latest models or more
abstractions, but about evaluation and measurement discipline.

## Other data sources and bigger ideas

- **Additional corpora to index** beyond personal notes — OpenZIM Wikipedia
  subsets (the eval harness already targets a Wikipedia top-100 public set);
  Claude Code / Claude Desktop conversation ingestion (append-only — old
  conversations get deleted upstream, and resuming a conversation can change
  its title, so ingestion must add, never delete).
- **Code indexing** — index a whole codebase; embedding code needs
  code-aware chunking (tree-sitter). Speculative.
- **Keeping it warm** — a daemon so the system stays responsive.
- **Minerva as a skill + subagent** — a skill giving precise search
  instructions and a subagent to save context, gathering more before returning
  a summary.
