---
slug: 2026-05-17-eval-harness-phase-1
created: 2026-05-17T14:00:00Z
last_updated: 2026-05-18T18:00:00Z
status: finalized
---

# Eval Harness — Phase 1 of Minerva RAG Implementation Roadmap

## Goal

Build `Minerva.Search.Bench`, a durable retrieval-evaluation harness for the
Minerva RAG pipeline. This is Phase 1 of the implementation roadmap and is
explicitly designed to remain in use through Phases 2, 3, 5, and 6 (rerank,
chunk size, dedup, K-tuning) and to support cross-model comparisons
(embedding models, contextualization strategies). Phase 4 (expansion)
requires an additional metric — document-level Recall@K is blind to
expansion benefit — and that metric will be specified when Phase 4 starts. Phase 1 ships the harness plus a
committed Phase 0 baseline so all subsequent changes have a fixed reference
to be judged against.

## Constraints and Non-Goals

- **Retrieval metrics only.** Recall@K and MRR. Answer-quality metrics
  (LLM-as-judge, RAGAS-style context precision/recall) are out of scope for
  Phase 1.
- **No expansion-eval in Phase 1.** Document-level ground truth is blind to
  whether ±W expansion improves answers. This is a known gap, deferred to
  Phase 4 with two candidate approaches recorded below.
- **No statistical significance testing.** With 20–30 queries variance is
  high; rely on per-query diffs, not p-values.
- **No auto-generated query banks at scale.** Hand-curated seed set; LLM
  assistance only with manual review.
- **No UI or dashboard.** CSV/JSONL output plus a starter Jupyter notebook is
  the entire analysis surface.
- **Personal-notes datasets and their results must never enter the public
  repo.**
- **Tiny synthetic corpora (10–20 docs) are explicitly out.** Too small to
  make retrieval non-trivial; results would not generalize at all.

## Decisions

### Done-definition: code + seeded eval set + committed Phase 0 baseline

**Choice**: Phase 1 ships when (a) the bench binary works end-to-end, (b)
the hand-curated eval JSONL is committed (`eval/datasets/...`) along with
a sibling README documenting how to obtain and ingest the underlying
public corpus — URL, content hash, license, ingestion command — (c) the
Phase 0 baseline CSV produced by the maintainer's run against that corpus
is committed as a regression reference.

**Rationale**: Distinguishes what to mirror from what to link. The corpus
is large and has a canonical upstream, so commit instructions, not bytes.
The eval JSONL is hand-authored — no upstream exists, so it must live in
the repo or the eval can't be reproduced. The baseline CSV is the
maintainer's measurement, useful as a reference point. Personal-notes
dataset work happens in parallel — valuable but doesn't gate Phase 1.

### All-.NET bench, with a starter Jupyter notebook for analysis

**Choice**: `src/Minerva.Search.Bench/` is a .NET console project sibling to
`src/Minerva.Search.Cli/`. `eval/notebooks/` ships with at least one
starter notebook that loads the committed Phase 0 baseline CSV, prints the
Recall@K table, plots Recall vs HybridAlpha, and surfaces per-query
regressions.
**Rationale**: The bench needs deep in-process access to `SearchPipeline`'s
per-stage state (semantic hits, BM25 hits, RRF survivors, eventually
reranker scores) — serializing all of that across an IPC boundary for
Python aggregation would be friction every iteration. The notebook layer
gives bounded exposure to the field-standard analysis toolkit
(pandas/matplotlib) without contaminating the engine, and forces us to
verify the CSV format is genuinely pandas-friendly. Notebooks for end users
remain optional; the starter notebook is a Phase 1 deliverable.

### Document-level ground truth

**Choice**: Each query's gold target is one or more **source documents**
(`gold_sources`). A retrieval is correct if any chunk from any gold source
appears in the top-K result.
**Rationale**: Chunk-level ground truth becomes invalid the moment the
corpus is re-chunked, and Phase 3 of the roadmap explicitly re-chunks.
Document-level survives re-chunking, re-contextualization, and re-embedding.
The Anthropic article uses chunk-level for their eval; we deliberately
diverge to make the harness durable.

### Bench invocation shape: one collection × one dataset × sweep matrix

**Choice**: A single bench invocation evaluates one collection against one
dataset, sweeping over a matrix of runtime parameters. Output is one dated
result directory. Cross-collection comparison (= comparing chunk configs or
embedding models) is done by running the bench multiple times and joining
the result CSVs in the notebook.
**Rationale**: Keeps the bench's job clean and avoids conflating two
fundamentally different kinds of sweeps. Runtime params (TopK, HybridAlpha,
future W) are cheap to vary against the same indexed collection. Indexing
params (chunk size, embedding model, contextualization on/off) require
re-ingesting into separate collections — that's external setup, not
in-process iteration.

### Sweep config as TOML files in `eval/sweeps/`

**Choice**: Sweep definitions live in TOML files checked into the repo (or
in the gitignored `eval/sweeps/private/` for personal runs). The bench CLI
is a thin wrapper: `bench run --sweep <path> --out <dir>`. Each sweep file
declares the dataset path, collection name, runtime-param matrix, and
fixed-param values.
**Rationale**: A sweep config is part of an experiment's identity. Treating
it as a versioned file in the repo means `eval/sweeps/phase0-baseline.toml`
is the literal recipe that reproduces the committed Phase 0 baseline CSV.
CLI-only sweeps live in shell history, not in the repo.

### Sweep covers runtime params only

**Choice**: The matrix can vary TopK, HybridAlpha, and (when Phase 4 lands)
W. It cannot vary chunk size, embedding model, or contextualization
strategy.
**Rationale**: Same as above — indexing params change which collection you
run against, not the runtime config. Comparing across them is a
notebook-side join across multiple bench runs.

### Three-file output per invocation

**Choice**: Each invocation writes a dated directory containing:

```text
results/2026-05-18T14-30-22_notes-v1_phase0/
  run.json        # manifest: collection, dataset path, resolved sweep,
                  #   timestamp, git SHA, embedding model id+version,
                  #   contextualization prompt info, indexer commit
  metrics.csv     # one row per (query × sweep-cell)
                  #   columns: query_id, topk, hybrid_alpha, [other knobs],
                  #            recall_at_5, recall_at_10, recall_at_20,
                  #            mrr, latency_ms
  details.jsonl   # one record per (query × sweep-cell)
                  #   per-stage survivors: semantic top-N, BM25 top-N, RRF
                  #   fused list with scores, final top-K, gold sources
                  #   hit / missed
```

**Rationale**: Clean separation of concerns. `run.json` answers "what
produced this?", `metrics.csv` is the flat pandas-friendly artifact for
aggregation, `details.jsonl` is heavy and only opened when investigating a
specific regression. Aggregate metrics across all queries are computed in
the notebook, not stored — derivable from `metrics.csv`, single source of
truth.

### Metrics: Recall@5, Recall@10, Recall@20, MRR@10

**Choice**: Per-query metrics stored in `metrics.csv`. Recall@K is
document-level (1 if any chunk from any gold source appears in top-K, else
0). MRR@10 is the reciprocal rank of the first chunk from any gold source
within the top-10, capped at 10.
**Rationale**: Recall@5/@10/@20 is the headline retrieval metric and what
Anthropic reports. MRR captures *how high* the first gold hit ranks, which
matters for distinguishing reranker variants in Phase 2. The 1-Recall@20
metric from the Anthropic article is computable as `1 - mean(recall_at_20)`
from the same data — no extra column needed.

### Expansion-eval gap acknowledged and deferred

**Choice**: Document-level Recall@K cannot detect whether ±W chunk expansion
improves answers. Phase 1 records this explicitly and defers the eval
design to Phase 4. Two candidate approaches are recorded for future choice:

- **LLM-as-judge over `answer_text`**: small judge LLM scores whether the
  final payload (chunks + expansion) is sufficient/correct to produce the
  recorded answer.
- **Section-coverage via `gold_sections`**: payload-coverage metric — did
  the retrieved+expanded payload include the text under the gold section
  heading? Heading-anchored, stable across re-chunking.

Both, either, or a different approach are acceptable; decision deferred.
**Rationale**: Designing the expansion eval before Phase 0 is even
baselined would be premature. Phase 1's job is to baseline Phase 0 and
unblock Phases 2 and 3 (both of which the document-level retrieval metrics
do cover). Recording both candidates now keeps the artifact honest about
what we know we'll need.

### Privacy via parallel `private/` subdirectories

**Choice**:

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
    ...
```

`.gitignore` entries: `eval/datasets/private/`, `eval/sweeps/private/`,
`eval/results/`.
**Rationale**: Clean separation by directory beats per-file ignore rules.
Mistakes are localized — if you accidentally put a personal dataset
outside `private/`, it would be obvious in review. `eval/results/` is
always ignored because `details.jsonl` leaks query text and chunk content,
regardless of whether the source was public or private.

### Dataset schema (JSONL)

**Choice**:

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

Required: `id`, `query`, `gold_sources` (at least one).
Optional: `answer_text`, `notes`.
`gold_sections` is **reserved for Phase 4** — its format and use will be
decided alongside the expansion-eval metric. Do not populate it in Phase 1
datasets; data authored against a format we haven't pinned would likely be
unusable later.

**Canonical form of `gold_sources` entries**: POSIX relative path from the
corpus root, case-sensitive, no leading `./`. Ingestion must record this
exact string per chunk so eval-time lookups resolve without normalization.
This is the load-bearing contract between ingestion and the bench; without
it, mismatches silently zero out recall and look like retrieval failures.

**Rationale**: Schema lifted from the prior considerations doc with two
tweaks — the per-record `collection` field is dropped (which collection a
dataset targets is a property of the *file*, named by the sweep config,
not of each query), and `gold_sections` is reserved for Phase 4 to avoid
authoring data against an unpinned format.

### Phase 0 baseline configuration is pinned

**Choice**: Contextualization on, dedupe off, default chunker, default
embedder. Recorded explicitly in the baseline sweep file's comments.
**Rationale**: The committed baseline can't be reproducible if its
pipeline configuration is left open. This is the reference every later
phase will be measured against.

### Phase 1 CLI surface is closed at `run` + `validate-dataset`

**Choice**: Phase 1 ships `bench run` and `bench validate-dataset`. Other
verbs (`diff`, `summary`, ...) are explicitly deferred.
**Rationale**: Scope discipline. `validate-dataset` is in scope because it
defends the canonical `gold_sources` contract (above); the rest can wait.

### Bench inherits configuration from `Minerva.Search.Cli`

**Choice**: Connection strings, DB config, and related settings come from
the same mechanism `Minerva.Search.Cli` already uses.
**Rationale**: One integration surface; no parallel config story to
maintain.

## Approach Preferences

- **Reproducibility means the *procedure* is reproducible**, not the
  numbers. Any third party can clone the repo, ingest the documented
  public corpus into a Minerva collection, and run
  `bench run --sweep eval/sweeps/phase0-baseline.toml` to produce their
  own baseline CSV. The committed baseline is one such instance from the
  maintainer's machine — a reference point for the maintainer's own
  regression testing, not a value others must match. Cross-machine
  variance (HNSW build non-determinism, hardware, library versions) is
  expected and unquantified.
- **`run.json` is self-describing.** It embeds the resolved sweep config and
  every version/identifier needed to interpret the run: embedding model id
  and version, contextualization prompt info, indexer commit, bench commit,
  database schema version. Pin what can be pinned.
- **Determinism where possible.** Fixed embedding model version, fixed
  reranker version (when added), fixed seed for any stochastic step. Where
  determinism is impossible (e.g. external API non-determinism), document
  it.
- **Absolute retrieval numbers don't transfer across corpus sizes;
  relative comparisons within a dataset do.** A 95% Recall@10 on the
  public dataset does not predict 95% on personal notes. Always re-run
  changes on both the public and personal datasets when both exist; a
  change that helps one and hurts the other is a corpus-dependent finding,
  not a universal improvement.
- **Start small, then expand the dataset.** Author ~5 queries first, get
  the bench producing sane numbers on them, then grow to 20–30. The
  considerations doc's point stands: a dozen small issues (path handling,
  source ID normalization, "what counts as a hit") are much cheaper to
  fix at 5 queries than at 30.
- **One change at a time, always run the full eval set.** A change that
  helps query #7 may silently hurt query #23. Per-query diffs in the
  notebook are the primary debugging surface, not aggregate deltas.
- **CSV/JSONL is the contract; notebooks are downstream.** The committed
  starter notebook is for learning and convenience. Anyone preferring
  Excel, .NET tools, or different Python tooling can ignore it without
  losing capability.

## Open Questions

- [ ] **Seed corpus selection.** Preference is kiwix Wikipedia top-100;
  contingent on verifying a stable URL with a content hash, license
  compatibility, and acceptable ingestion time on a developer machine. If
  blocked, fall back to a curated public Markdown set (still medium-sized,
  not tiny).
- [ ] **Provenance fields in `run.json`.** Concrete list of which
  identifiers and versions must be captured to honor the reproducibility
  claim. At minimum: bench commit SHA, indexer commit SHA, embedding model
  id+version, contextualization model+prompt version, database schema
  version, resolved sweep config. May also need: timing data, host info
  (for latency comparability).
- [ ] **Sweep config TOML schema.** Knob names, range/list/grid syntax,
  fixed-param syntax, how the dataset path and collection name are
  declared. Should be documented with a worked example
  (`phase0-baseline.toml`) before the bench is built.
- [ ] **`details.jsonl` schema.** Which scores to record at each stage,
  chunk identifier format (chunk_id? source_id+offset?), size limits if
  any, whether to record full chunk text or just IDs. Trade-off: full text
  makes debugging easy but explodes file size.
- [ ] **Phase 4 expansion-eval choice.** Pick between LLM-as-judge over
  `answer_text`, section-coverage via `gold_sections`, or both. Defer
  until just before Phase 4 starts.

## Research Findings

- **Anthropic contextual retrieval article**: their eval uses chunk-level
  ground truth and reports retrieval recall @ K (notably 1 - Recall@20).
  Phase 1 deliberately diverges to document-level ground truth for
  durability across re-chunking; the 1-Recall@20 metric remains computable
  from our stored data.
- **Prior `eval-harness-considerations.md`**: an earlier conversation that
  outlined the dataset schema, metric choices (Recall@K + MRR@10),
  dataset-authoring tips, and anti-patterns to avoid. Treated as accepted
  baseline; the only deviation is dropping the per-record `collection`
  field.
- **`plan.md` from `.dev/2026-05-16-roadmap-phase-1/`**: commits to
  `Minerva.Search.Cli` and `Minerva.Search.Bench` as sibling .NET projects.
  CLI is single-shot for manual exploration; Bench is the harness. This
  artifact builds on that decision.
- **Roadmap (`2026-05-16-minerva-rag-pipeline-implementation-roadmap.md`)**:
  Phase 1's stated goal is the eval harness; Phase 2 (rerank) is the next
  consumer; Phase 3 (chunk size) is the reason document-level ground truth
  matters; Phase 4 (expansion) is the reason the expansion-eval gap is
  documented now rather than discovered later.
