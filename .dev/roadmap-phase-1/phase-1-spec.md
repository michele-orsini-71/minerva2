---
slug: phase-1-spec
title: Phase 1 — Eval Harness — Specification
status: finalized
parent: ../new-implementation-roadmap/2026-05-16-minerva-rag-pipeline-implementation-roadmap.md
distilled_from:
  - archive/2026-05-17-eval-harness-plan.md
  - archive/2026-05-18-eval-harness-implementation-phases.md
  - archive/2026-05-19-dataset-authoring-helper.md
---

# Phase 1 — Eval Harness — Specification

The durable "what we decided and why" for Phase 1. Status of the work lives
in `phase-1-progress.md`; this document records the decisions that outlive
the implementation.

## Goal

Build `Minerva.Search.Bench`, a durable retrieval-evaluation harness for the
Minerva RAG pipeline. It is explicitly designed to remain in use through
Phases 2, 3, 5, and 6 (rerank, chunk size, dedup, K-tuning) and to support
cross-model comparisons (embedding models, contextualization strategies).
Phase 1 also ships a committed Phase 0 baseline so every later change has a
fixed reference to be judged against.

## Done-definition

Phase 1 ships when:

1. The bench binary works end-to-end.
2. The hand-curated eval JSONL is committed under `eval/datasets/`, with a
   sibling README documenting how to obtain and ingest the underlying public
   corpus — URL, content hash, license, ingestion command.
3. The Phase 0 baseline CSV produced by the maintainer's run against that
   corpus is committed as a regression reference.

**Reproducibility means the _procedure_ is reproducible, not the numbers.**
Any third party can clone the repo, ingest the documented public corpus, and
run `bench run --sweep eval/sweeps/phase0-baseline.toml` to produce their own
baseline. Cross-machine variance (HNSW build non-determinism, hardware,
library versions) is expected and unquantified.

## Constraints and non-goals

- **Retrieval metrics only.** Recall@K and MRR. Answer-quality metrics
  (LLM-as-judge, RAGAS-style context precision/recall) are out of scope for
  Phase 1 (LLM-as-judge is re-opened as the optional slice 1E-β).
- **No expansion-eval in Phase 1.** Document-level ground truth is blind to
  whether ±W expansion improves answers. Known gap, deferred to Phase 4.
- **No statistical significance testing.** With 20–30 queries variance is
  high; rely on per-query diffs, not p-values.
- **No auto-generated query banks at scale.** Hand-curated seed set; LLM
  assistance only with manual review (the optional slice 1E-α).
- **No UI or dashboard.** CSV/JSONL output plus a starter Jupyter notebook is
  the entire analysis surface.
- **Personal-notes datasets and their results must never enter the public
  repo.**
- **Tiny synthetic corpora (10–20 docs) are out.** Too small to make
  retrieval non-trivial; results would not generalize.
- **Ingestor versioning / reindex policy is out of scope** for the harness —
  it is a Minerva-product question, now tracked on the roadmap.

## Architecture decisions

### All-.NET bench, with a starter Jupyter notebook

`src/Minerva.Search.Bench/` is a .NET console project sibling to
`src/Minerva.Search.Cli/`. The bench needs deep in-process access to
`SearchPipeline`'s per-stage state (semantic hits, BM25 hits, RRF survivors,
later reranker scores); serializing that across an IPC boundary for Python
would be friction every iteration. The notebook layer (`eval/notebooks/`)
gives bounded exposure to pandas/matplotlib and forces verification that the
CSV is genuinely pandas-friendly. **CSV/JSONL is the contract; notebooks are
downstream** and optional.

### Bench inherits configuration from `Minerva.Search.Cli`

Connection strings, DB config, and related settings come from the same
mechanism Cli already uses. One integration surface, no parallel config
story. The composition root reuses `MinervaSearchBuilder.CreateAsync`.

### Bench invocation shape: one collection × one dataset × sweep matrix

A single invocation evaluates one collection against one dataset, sweeping a
matrix of **runtime** parameters, and writes one dated result directory.
Cross-collection comparison (different chunk configs or embedding models) is
done by running the bench multiple times and joining result CSVs in the
notebook. Runtime params (TopK, HybridAlpha, future W) are cheap to vary
against the same indexed collection; indexing params (chunk size, embedding
model, contextualization on/off) require re-ingesting into separate
collections — external setup, not in-process iteration.

### Document-level ground truth

Each query's gold target is one or more **source documents**
(`gold_sources`). A retrieval is correct if any chunk from any gold source
appears in the top-K result. Chunk-level ground truth becomes invalid the
moment the corpus is re-chunked, and Phase 3 explicitly re-chunks.
Document-level survives re-chunking, re-contextualization, and re-embedding.
The Anthropic article uses chunk-level for its eval; we deliberately diverge
for durability. The `1 - Recall@20` metric they report remains computable
from our stored data.

### Sweep config as TOML files in `eval/sweeps/`

A sweep config is part of an experiment's identity, so it is a versioned file
in the repo. `eval/sweeps/phase0-baseline.toml` is the literal recipe that
reproduces the committed Phase 0 baseline CSV. The CLI is a thin wrapper:
`bench run --sweep <path> --out <dir>`. **Sweep covers runtime params only**
— it can vary `top_k`, `hybrid_alpha`, and (Phase 4) `W`; it cannot vary
chunk size, embedding model, or contextualization strategy.

**TOML shape:** every knob lives in `[matrix]` as a list, even singletons.
snake_case names. Top-level `dataset` and `collection` scalars. Worked
example:

```toml
dataset = "eval/datasets/wikipedia-top100-v1.jsonl"
collection = "wikipedia-top100-v1"

[matrix]
top_k = [10]
hybrid_alpha = [0.3, 0.5, 0.7]
```

### Three-file output per invocation

Each invocation writes a dated directory:

```text
results/<timestamp>_<dataset-slug>/
  run.json      # manifest: what produced this run
  metrics.csv   # one row per (query × sweep-cell); the flat pandas artifact
  details.jsonl # one record per (query × sweep-cell); heavy, per-stage survivors
```

Clean separation: `run.json` answers "what produced this?", `metrics.csv` is
the aggregation surface, `details.jsonl` is opened only when investigating a
specific regression. Aggregate metrics are computed in the notebook, not
stored — derivable from `metrics.csv`, single source of truth.

**`run.json` provenance — tier 1 (shipped):** `timestamp` (UTC ISO 8601),
`bench_version` (SemVer + short git SHA, baked at build via
`AssemblyInformationalVersion`), `dataset_path`, `collection`,
`resolved_sweep`, `cells` (enumerated Cartesian product).
**Tier 2:** the collection's full provenance block — embedding model +
dimension, chunker config, contextualization model + the two prompt versions,
ingestor commit SHA, DB schema version. Sourced from `collection_metadata` and
stamped into `run.json` by Phase D of
[`collection-metadata-design.md`](collection-metadata-design.md).

### Metrics: Recall@5, Recall@10, Recall@20, MRR@10

Per-query, stored in `metrics.csv`. Recall@K is document-level (1 if any
chunk from any gold source is in top-K, else 0). MRR@10 is the reciprocal
rank of the first gold-source chunk within top-10, else 0. Recall@5/10/20 is
the headline metric Anthropic reports; MRR captures _how high_ the first hit
ranks, which matters for distinguishing reranker variants in Phase 2.

### Phase 0 baseline configuration is pinned

Contextualization on, dedupe off, default chunker, default embedder. Recorded
in the baseline sweep file's comments. The committed baseline cannot be
reproducible if its pipeline config is left open.

### Phase 1 CLI surface

`bench run` and `bench validate-dataset` are the closed Phase 1 surface.
`author-dataset` was added as a dataset-authoring helper (see below). Other
verbs (`diff`, `summary`, `gen-queries`) are deferred; `gen-queries` is the
optional slice 1E-α.

## Dataset schema (JSONL)

```json
{
  "id": "q001",
  "query": "What did the 2024 ARERA decision change about Scambio sul Posto?",
  "gold_sources": ["notes/energy/arera-2024-ssp.md"],
  "gold_sections": ["notes/energy/arera-2024-ssp.md#decision-2024-456"],
  "answer_text": "...",
  "notes": "regression case from Brexit-style dedupe incident"
}
```

Required: `id` (unique across file), `query` (non-empty), `gold_sources` (at
least one). Optional: `answer_text`, `notes`. **`gold_sections` is reserved
for Phase 4** — its format will be decided alongside the expansion-eval
metric; do not populate it in Phase 1 datasets.

**Canonical form of `gold_sources` entries:** opaque indexer-assigned
`source_id` strings. The bench treats them as black-box strings and resolves
them by equality against `chunks.source_id` in Postgres for the target
collection — no path semantics, no normalization. The load-bearing contract
is two properties of the indexer: (a) source_ids are stable across
re-ingestion of the same logical document, (b) they are stored in Postgres
alongside each chunk. A dataset is implicitly bound to the collection it was
authored against, since the same logical document gets different source_ids
under different indexers. **A dataset is not bound to one collection** in the
file: the same `gold_sources` are valid against any collection that indexed
the same logical corpus (e.g. the same Wikipedia dump contextualized by
different LLMs); the collection is chosen by the operator at invocation time.

## Privacy via parallel `private/` subdirectories

```text
eval/
  datasets/
    wikipedia-top100-v1.jsonl     # committed
    private/                       # gitignored
      personal-notes-v1.jsonl
  sweeps/
    phase0-baseline.toml          # committed
    private/                       # gitignored
      phase0-personal.toml
  results/                         # gitignored entirely
```

`.gitignore`: `eval/datasets/private/`, `eval/sweeps/private/`,
`eval/results/`. `eval/results/` is always ignored because `details.jsonl`
leaks query text and chunk content regardless of source.

## Dataset authoring helper (`author-dataset`)

A one-shot subcommand of the bench for authoring eval entries against a
single source per run. The format, validator, and DB wiring already live in
the bench, so co-locating keeps configuration trivial.

`Minerva.Search.Bench author-dataset --file <path> --collection <name>`
(both required, session-wide).

Linear prompt flow: search query → run the full pipeline, dedupe by
`source_id` keeping the best-scoring chunk per source, display
`[rank] source-id score excerpt` → pick a source by rank → question loop
where each non-empty line buffers an entry `{id, query, gold_sources:[id]}`,
empty line ends → append all buffered entries in one write, print a session
summary, exit.

- **IDs:** auto-generated `<source-id>-<n>`, `n` the next 1-based index for
  that source; seeded from existing entries on startup, collision-tolerant.
- **Buffer in memory, write once on exit:** abandoning the run (Ctrl-C)
  discards typos — an implicit undo.
- **Malformed existing JSONL at startup:** abort with a clear error pointing
  to the offending line, rather than silently desyncing counters.
- v1 writes only `id`, `query`, `gold_sources` — never `gold_sections`,
  `notes`, or `answer_text`. One source per run; relaunch for another.

## Method: how to read eval results

Lifted from the original considerations so the discipline survives.

- **Aggregates tell you direction only.** With 20–30 queries a 1–2pp change
  is in the noise floor; a 5+pp change is meaningful. Roadmap predicts the
  shape: Phase 2 (rerank) should move Recall@10 noticeably (+5–10pp) — if it
  does not, something is wrong, not noise. Phase 6 (K-tuning) has small
  effects where aggregates cannot be trusted.
- **The per-query diff is the real decision tool.** Weigh wins vs losses by
  _what kind_ of query they are, not by counting. 21 generic wins + 1 loss on
  a query you specifically added as a regression test can mean "don't ship".
- **Treat the eval as a regression net, not a scoreboard.** Every query is in
  there for a reason; a state change is a story to read, not a number to
  average.
- **Calibrate the noise floor.** Run the same pipeline twice; the aggregate
  delta between identical runs (HNSW build noise) is your floor.
- **Comparisons are phase-to-phase**, not always-vs-Phase-0. Phase 2 vs 0,
  Phase 3 vs 2, etc. Occasionally check cumulative vs Phase 0.

## Open questions

- [ ] **Seed corpus selection.** Preference: kiwix Wikipedia top-100,
  contingent on a stable URL with content hash, license compatibility, and
  acceptable ingestion time. Fallback: a curated public Markdown set.
- [ ] **`details.jsonl` schema.** Which per-stage scores to record, chunk
  identifier format, whether to store full chunk text or IDs only, size
  limits. Trade-off: full text eases debugging but explodes file size.
- [ ] **Phase 4 expansion-eval choice.** LLM-as-judge over `answer_text`,
  section-coverage via `gold_sections`, or both. Deferred until just before
  Phase 4. Note: slice 1E-β may absorb the LLM-as-judge candidate.
