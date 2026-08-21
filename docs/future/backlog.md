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
  - *Finding (1D FTS-query eval).* The current FTS — `ts_rank` over
    `to_tsvector('simple', …)` with OR-combined terms — is **not** a BM25
    substitute. `ts_rank` weights by term *frequency*, not IDF, so a rare exact
    token (code, designation, brand name) gets no rarity boost. A bare-acronym
    query returns its unique gold at rank 1, but the same acronym inside a
    natural-language sentence sinks to rank 9–35 at FTS-heavy `alpha=0.3`:
    `'simple'` keeps stopwords, terms are OR'd, so common filler words match
    competitor docs and out-`ts_rank` the single rare-token hit. The lexical
    branch therefore cannot deliver "vector misses, FTS saves on a rare code"
    unless the query is almost all discriminating tokens. Real BM25
    (ParadeDB / `pg_search`, or explicit IDF weighting) is a **ship
    requirement**, not optional. Evidence: the FTS-specific queries in
    `eval/datasets/wikipedia-v1.jsonl`, reproduced by `fts_search.sh`.
- **Ingestor versioning / reindex policy** → roadmap "Cross-phase backlog";
  **`collection_metadata`** → designed in
  `../../.dev/roadmap-phase-1/collection-metadata-design.md` (pre-1D).

## Retrieval and answer quality

- **MCP tool surface as affordances (design principle).** Minerva is
  tool-shaped: a capable consuming agent owns the control flow — query
  rewriting, sub-question decomposition, multi-hop, retry — so the product
  ceiling is what the MCP *exposes*, not how many pipeline stages exist inside.
  Prefer exposing retrieval capabilities as composable tools the agent picks
  per question — `search(filters)`, `expand_chunk(±W)` as an *on-demand* call
  (not only the fixed Phase-4 stage), `get_document_outline` (return a
  document's heading structure so the agent can navigate before fetching),
  `fetch_full_doc` — over hardcoding them as opaque internal stages the agent
  cannot see or skip. A server that returns top-k opaque chunks caps a strong
  agent no matter how good the ranking. This reframes several items below
  (**Metadata-filter search**, **Document retriever mode**, **Return raw search
  results to the client**) as facets of one surface-design concern, and it is
  the reason **Query understanding / expansion** below is low priority: a strong
  agent already does it. Caveat: the whole bet assumes a *strong* consuming
  agent; point a weak agent at Minerva and the missing pipeline stages become
  visible again. Of the tools named here, `get_document_outline` and on-demand
  `expand_chunk` / `fetch_full_doc` are not yet designed anywhere; the filter
  and raw-results facets are the items below.
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
- **Metadata-based ranking (recency / freshness)** — use note metadata to
  influence the score, not only to filter; e.g. rank newer notes higher via a
  recency boost or time decay. Distinct from the **Metadata-filter search** item
  above (filtering includes/excludes; this changes the order). Open design
  questions: which timestamp is the signal (note modification time from
  frontmatter vs. chunk/collection ingest `last_updated_at`, which differ), and
  whether recency is a multiplicative boost on the fused score or a separate
  signal fused alongside dense/FTS. The infrastructure already exists (timestamp
  columns), but no algorithm is designed. Surfaced as a one-line note —
  "last updated time puo' essere rilevante per il ranking" — in
  `../../.dev/roadmap-phase-1/archive/2026-06-10-more-considerations.md`, tied to
  reranking (Phase 2).
- **Document retriever mode** — given the index, return the note (or list of
  notes) that discuss a topic, as an alternative to a summarized answer. A
  "search that returns sources", useful for tools like Obsidian.
- **Return raw search results to the client** — instead of always handing the
  result to an LLM for summarization, optionally return the ranked hits for the
  client to render, with client-side filtering (exclude paths, include/exclude
  archive).
- **Trigram matching (`pg_trgm`) as a fuzzy lexical signal** — PostgreSQL's
  built-in `pg_trgm` (character trigrams, GIN/GiST indexes for `similarity`
  and fast `ILIKE`) offers typo tolerance (`Mnerva` ≈ `Minerva`) and partial
  matching on proper nouns and technical terms. Notably, trigram overlap is a
  *language-independent approximation of stemming* (`russo`/`russi` share
  trigrams), so it partially addresses the `'simple'`-config gap that
  **per-document language detection** targets (see
  `../reference/full-text-search.md`), without detecting the language. It is a
  complement, not a replacement: it is not BM25 (no IDF/length normalization)
  and is weak at ranking long chunks, being built for short strings. Open
  design question: how it fuses — a separate signal in rank fusion, or only a
  fuzzy fallback for entity/term matching. Sits beside the **Real BM25** and
  language-detection cross-phase items.
- **Summarizer / contextualizer quality** — the Anthropic recipe passes the
  whole document with each chunk; Minerva uses a document summary for time and
  cost. Could improve by constraining the summary to a size relative to the
  source, or a fixed budget.

## Eval harness — metrics

- **Average Precision (AP) / Mean Average Precision (MAP)** — a rank-sensitive
  information-retrieval metric that averages precision at each rank where a
  relevant document is retrieved (MAP is the mean across queries). Considered
  and deferred, not adopted. For single-gold queries AP reduces exactly to the
  reciprocal rank, so it duplicates the existing MRR@10; it adds information
  only for multi-gold queries, where it rewards ranking *all* gold sources high
  rather than just the first. It also assumes reasonably *complete* relevance
  judgments: every retrieved non-gold document counts as a miss, so with the
  small hand-curated `gold_sources` set (likely incomplete — relevant but
  unlabelled documents exist) MAP can be distorted. This is the same reason
  Anthropic's contextual-retrieval eval reported Recall@20 rather than MAP. The
  current metric set (Recall@K, Success@K, MRR@10 — see
  `../../.dev/roadmap-phase-1/phase-1-spec.md`) already covers the single-gold
  and coverage cases and is more robust to incomplete labels. Revisit only if a
  large multi-gold query set with fairly complete judgments is built. nDCG is a
  related graded-relevance option, but relevance here is binary, so it would
  offer little over the above.

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
- **ZIM ingestion path — JSON notes / `MinervaV1Indexer` instead of the markdown
  shortcut.** Current path is a deliberate shortcut: `zim → kiwix2md tool →
  markdown files → MarkdownIndexer`. It works and produces the Wikipedia corpus
  on demand. A `ZimIndexer` was not built directly because `libzim` is a Python
  library. Three alternatives, kept as options, none planned: (1) have the Python
  tool emit JSON notes like the ones used in `../minerva` instead of markdown;
  (2) make the Python tool a proper `../minerva` extractor; (3) build a
  `MinervaV1Indexer` in minerva2 that reuses the old (Python) extractors. Why
  deferred: the Reranker is the target, and the path does not serve it — the same
  corpus text reaches the index regardless of how documents entered, so retrieval
  and ranking quality are unaffected. Metadata is not a reason to switch either:
  frontmatter already flows through `MarkdownIndexer` into `Document.Metadata`,
  so the metadata-ranking and metadata-filter ideas above can be fed from
  frontmatter without JSON notes. The real cost of the JSON path is a second
  ingestion architecture and early cross-lineage coupling between minerva2 (.NET)
  and minerva (Python) extractor schemas, which are meant to version
  independently. Cheap insurance to keep this option open at near-zero cost: in
  the `kiwix2md` tool, keep content *extraction* separate from markdown
  *emission*, so "emit JSON notes instead" later becomes swapping the emitter,
  not a rewrite.

## Clients and feature parity

- **A first non-watcher client** — an MCP server or a CLI/GUI tool to exercise
  and inspect `SearchAsync`. For small documents, returning the whole document
  rather than chunks is a useful variant.
- **Feature parity with the original (Python) Minerva** — collect the v1
  commands (`index`, `serve`, `serve-http`, `peek`, `remove`, `validate`,
  `query`, `keychain`; and `minerva-kb`:
  `add/list/status/sync/watch/remove/serve`) and decide which still make sense
  for v2. Many (`peek`, `remove`) become thin wrappers; `validate` is obsolete.
  Open question: unify into a `MinervaServices` facade vs keep thin,
  unopinionated wrappers (it is open source — a user can fork).
- **Watcher orchestration GUI** — a tool to fire, configure, and monitor
  multiple watchers; deferred. Preflight is already a library capability any
  client can reuse.
- **Cross-module versioning** — keep the same version across modules; decide a
  policy.
- **Auto-rebuild on embedder change** — when a configured embedder produces a
  different vector dimension than an existing collection, the check fails with
  clear remediation and the user decides (drop + rebuild, or revert). Automatic
  rebuild is deferred.
- **Where the drift-recovery policy lives — core vs front-end.** Today core both
  *guards* and *recreates*: `PrepareCollectionAsync` compares the stored
  invariants against the configured set and, gated by a per-call
  `allowRecreateOnConfigMismatch` flag the front-end passes, either throws
  `CollectionConfigMismatchException` or drops-and-recreates. The destructive
  action lives in the library. An alternative separation: core becomes a pure
  always-throw guard (never deletes), and each front-end catches the exception
  and decides whether to wipe-and-reingest. This keeps the library from ever
  silently destroying data and makes recovery a client policy — relevant as more
  indexers (MCP, Obsidian) arrive, each potentially wanting different recovery
  behaviour. Does not affect the collection format, so it can be revisited
  anytime. Decide together with the review of previous (Python) Minerva
  behaviour, which is still pending.
- **Client-provenance schema versioning — stamp now, handle later.** The client
  bag (`kind` + opaque `data`) may change shape as new drivers and fields
  arrive. Versioning splits into two parts with different deadlines. The *stamp*
  — a monotonic `version` integer carried in the client envelope alongside
  `kind` — is the only part that must be in place *before the first real
  ingest*, because a discriminator cannot be added to already-written rows
  without a migration. Without it, a later version cannot tell old data apart,
  and a rolled-back older binary cannot detect that it is reading newer data.
  The *handler* — comparing the version, branching, migrating, or throwing — is
  deferred: it needs a real second shape to be written against, so building it
  now is migration code for a migration that does not exist. Two qualifiers keep
  this from over-building: (1) `ingestorVersion` (the git SHA in the last-run
  block) already records *which code* wrote a bag, just not a comparable schema
  number, so a dedicated integer is justified only if monotone comparison is
  actually wanted; (2) additive changes (a new optional field) are better
  absorbed by a *tolerant reader* (ignore unknown keys, tolerate missing ones)
  than by a version gate — reserve the gate for breaking changes (renamed,
  removed, or re-meant fields). Scale context: minerva2 is single-user, one
  ingestor binary at a time, one database, so there is no concurrent-version
  fleet; the only real axes are a new binary reading old data and a rollback
  reading newer data. Minimum decision to lock before deploy: every client bag
  carries `version = 1`; no comparison, no exception, no migration code yet.
  (Surfaced while building slice E; a version *check* was prototyped in
  `MarkdownIndexer.RunAsync` and rolled back as premature.)
- **Domain-exception family consistency (core + indexers).** Failures that are
  expected and operator-actionable should wear a type in the `MinervaException`
  family so top-level handlers can catch them as a category, rather than a raw
  framework type (`InvalidOperationException`, …) that slips into the generic
  crash bucket. Two concrete gaps surfaced while building slice E. (1)
  `PostgresCollectionRepository.ReadCollection` throws
  `InvalidOperationException` on corrupt/unreadable stored metadata (two exit
  sites: the `??` throw and the `catch … when` wrapper). No existing
  `MinervaException` subtype fits "stored data is corrupt" —
  `ConfigurationException` means *user misconfig*, not data integrity — so
  closing this implies a small new concrete type (e.g.
  `CollectionMetadataException`), a new-class decision deferred deliberately.
  Both throw sites should then use it and keep passing the caught exception as
  `inner`. (2) The markdown indexer's own exceptions
  (`NotAnIndexerCollectionException`, `CollectionPathChangeNotAllowedException`,
  and any future ones) currently fall through `Program.cs`'s generic
  `catch (Exception)` instead of a family branch; an
  `abstract MarkdownIndexerException` base plus one
  `catch (MarkdownIndexerException)` branch would report them as clean config
  errors with their own exit code. Neither is on the slice-E critical path; both
  are pure hygiene and revisit before deploy.
- **Indexer source-root guard is untested.** Slice E added a reingest guard in
  `MarkdownIndexer.RunAsync` (`kind` mismatch blocks, `sourceRoot` change is
  gated by `AllowSourceRootChange`, globs ignored). The literal-API-key check is
  covered by unit tests on `CollectionManager`, but the guard's own behaviour —
  "changed source root blocks; override proceeds; foreign/missing client
  provenance throws" — has no test. Two blockers, each a decision: (1) there is
  no `Minerva.MarkdownIndexer.Tests` project — create one, or fold the tests
  into `Minerva.Tests` with a project reference; (2) `RunAsync` is not testable
  as written — it constructs a real `MarkdownScanner` (filesystem), so the guard
  path cannot be exercised without either making the scanner injectable or
  extracting the guard into a unit that runs without disk access (it needs only
  `ISearchEngine`/`IIngestEngine` substitutes). Deferred pending those two
  decisions; the guard itself is implemented and works in manual runs.

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
6. **Per-collection embedding-model fingerprint** — subsumed by the
   `collection_metadata` work, designed in
   `../../.dev/roadmap-phase-1/collection-metadata-design.md` (the embedding
   model and dimension are guarded invariants in the provenance block).

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
