# Eval Harness — Implementation Phases

Derived from `2026-05-17-eval-harness-phase-1.md` (the project brief).
Phases are vertical slices, each producing something observable. Details
are intentionally minimal — they will be expanded when each phase is
about to start.

---

## Phase 1A — Skeleton + dataset validation

**Deliverable:** `Minerva.Search.Bench` project exists, inherits
configuration from `Minerva.Search.Cli`, and `bench validate-dataset
<path>` works end-to-end.

**Why this boundary:** validates the canonical `gold_sources` contract
(load JSONL → resolve each entry against the indexed collection → report
mismatches) before any retrieval code runs. If the source-id format is
wrong, everything downstream is wrong silently.

**Done when:** a hand-written JSONL with ~3 queries against an existing
local collection passes validation, and a deliberately broken entry
produces a clear error.

---

## Phase 1B — Single-cell run path

**Deliverable:** `bench run --sweep <path>` works for the degenerate
case — a sweep config with one matrix cell, dataset of a few queries.
Loads TOML, calls `SearchPipeline.SearchAsync` per query, computes
Recall@K and MRR per query in .NET, emits the three output files
(`run.json`, `metrics.csv`, `details.jsonl`).

**Why this boundary:** proves the entire output contract before adding
sweep multiplication. Cheaper to fix file-format and provenance issues
at one cell than at nine.

**Done when:** running the bench against a hand-written 5-query JSONL
produces well-shaped output files, and a notebook can open `metrics.csv`
with pandas without manual fixing.

---

## Phase 1C — Sweep matrix + per-query metrics across cells

**Deliverable:** the bench runs the full Cartesian product of the TOML
`[matrix]`, emits one row per (query × cell) in `metrics.csv` and one
record per (query × cell) in `details.jsonl`. Stdout prints an
aggregate Recall@K summary per cell.

**Why this boundary:** this is the harness becoming actually useful.
After this slice, the bench is feature-complete for retrieval evaluation.

**Done when:** a small dataset × small matrix (e.g. 5 queries × 3
HybridAlpha values) produces 15 rows in `metrics.csv` and the stdout
summary shows three Recall@10 numbers that pass a smell test.

---

## Phase 1D — Ship: seed eval set, corpus README, committed baseline, starter notebook

**Deliverable:** the four artifacts the brief's Done-definition
requires —

- `eval/datasets/wikipedia-top100-v1.jsonl` (20–30 hand-curated queries
  with `gold_sources`).
- `eval/datasets/wikipedia-top100-v1.README.md` (corpus URL, content
  hash, license, ingestion command).
- `eval/results/phase0-baseline/` containing the maintainer's run
  output, committed.
- `eval/notebooks/phase0-analysis.ipynb` loading the baseline CSV and
  producing the Recall@K table, a Recall-vs-HybridAlpha plot, and a
  per-query view.

**Why this boundary:** every preceding phase is engine work; this one
is content + reference data + analysis. Splitting it out keeps the
engine work measurable against criteria that don't depend on dataset
authoring.

**Done when:** nuking local state, following the corpus README, and
re-running the bench produces output that the committed notebook can
analyze without edits.

---

## Out of scope

- Phase 4 expansion-eval design (LLM-as-judge or section-coverage) —
  deferred per the brief's Open Questions.
- Bench CLI verbs beyond `run` and `validate-dataset` — explicitly
  deferred.
- Personal-notes dataset and its baseline — parallel work, doesn't gate
  any of 1A–1D.
