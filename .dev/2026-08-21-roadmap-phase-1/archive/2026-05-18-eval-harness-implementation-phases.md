---
slug: 2026-05-18-eval-harness-implementation-phases
created: 2026-05-17T14:00:00Z
last_updated: 2026-05-25T00:00:00Z
parent: 2026-05-17-eval-harness-plan.md
---

# Eval Harness — Implementation Phases

Derived from `2026-05-17-eval-harness-phase-1.md` (the project brief).
Phases are vertical slices, each producing something observable. Details
are intentionally minimal — they will be expanded when each phase is
about to start.

---

## Phase 1A — Skeleton + dataset validation - COMPLETED

**Deliverable:** `Minerva.Search.Bench` project exists, inherits
configuration from `Minerva.Search.Cli`, and `bench validate-dataset
<path>` works end-to-end.

**Why this boundary:** validates the canonical `gold_sources` contract
(load JSONL → resolve each source_id against the operator-specified
collection in Postgres → report misses) before any retrieval code runs.
If a gold source_id doesn't resolve in the target collection, recall is
silently zeroed for that query and the failure looks like a retrieval
problem.

**DONE [2026-05-19-dataset-authoring-helper.md](2026-05-19-dataset-authoring-helper.md)**

**Done when:** a hand-written JSONL with ~3 queries, validated against
an existing local collection passed via `--collection`, passes; a
deliberately broken entry produces a clear error.

### Resolved decisions 2

- **CLI signature:** `minerva-bench validate-dataset <jsonl-path> --collection <name>`. Both required.
- **Composition root:** reuse `MinervaSearchBuilder.CreateAsync` exactly as Cli does. No leaner builder. The search-side preflight (DB + embedding) runs even on `validate-dataset` — acceptable cost.
- **Source-id existence lookup:** add `Task<bool> SourceIdExistsAsync(string collection, string sourceId, CancellationToken)` to `ISearchEngine`. No new interface, no new builder. If catalog-shaped methods accumulate later, extract `IChunkCatalog` then.
- **Validation scope: L3** — schema enforcement (required fields, `id` unique across file, `gold_sections` not populated, no unknown top-level keys) + content lint (empty/whitespace `query`, duplicate source_ids within an entry) + existence check (every `gold_sources` entry resolved via `SourceIdExistsAsync`).
- **Error aggregation:** collect-all, never stop-at-first.
- **Report:** plain-text stdout, grouped by entry (entry `id` or line number if `id` missing), `- <message>` per issue, final summary line. Exit code `0` pass, `2` validation failure, `1` unhandled exception.
- **Config inheritance:** Bench ships its own example `appsettings.json` mirroring Cli's shape; live configs live in operator's runtime folders outside the source tree (no linked or shared in-source file).
- **Project shape:** `src/Minerva.Search.Bench/`, `AssemblyName = minerva-bench`, hand-rolled arg parser per `SearchCliArgs` convention (no new dependency). Verb-dispatch shell built in 1A even with one verb, so 1B's `run` is purely additive. `PrintUsage` modeled on Cli's.
- **Testing:** unit tests for parse + L3 logic in `tests/Minerva.Tests/Search/Bench/`, against in-memory JSONL strings covering every schema/lint failure mode and JSON parse errors. One end-to-end integration test in `Minerva.IntegrationTests` that seeds a small collection and validates a JSONL with one passing entry and one source_id-miss entry; asserts exit code + report substring. Validator split so pure-logic part is a function over parsed entries (no engine), existence pass is a thin second pass — unit tests target only the pure part.

---

## Phase 1B — Single-cell run path - COMPLETED

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

### Resolved decisions 1

- **CLI signature:** `minerva-bench run --sweep <toml-path> --out <dir>`. Both required. `--out` is the parent directory; the bench mints a dated leaf inside it (e.g. `<out>/2026-05-25T14-30-22_<dataset-slug>/`).
- **Sweep TOML shape:** every knob lives in `[matrix]` as a list, even singletons. snake_case names. 1B's degenerate case is single-element lists; 1C lengthens them with no parser change.
- **`run.json` Tier 1 fields (in scope for 1B):** `timestamp` (UTC ISO 8601), `bench_version`, `dataset_path`, `collection`, `resolved_sweep` (parsed TOML re-serialized), `cells` (enumerated Cartesian product).
- **Bench versioning:** `AssemblyInformationalVersion` baked at build time as `<SemVer>+<short-git-sha>` (e.g. `0.1.0+abc1234`) via a hand-rolled MSBuild target reading `git rev-parse`. Same pattern applies to `Minerva.Search.Cli` (and to the ingestor when convenient). Decouples bench runtime from repo location, so the bench can be installed anywhere.
- **Collection-metadata fields deferred:** embedding model id+version, ingestor commit SHA, DB schema version are _not_ in 1B's `run.json`. They depend on a sibling task — a `collection_metadata` row/table in Postgres populated at ingest time. That task is scoped separately and should land before Phase 1D (between 1C and 1D if possible).
- **Ingestor versioning policy deferred:** the broader question of "what happens when ingestor code changes — reindex required? signature-enforced?" is a Minerva-product policy, not an eval-harness decision. To be added to the main roadmap immediately after Phase 1 ships.
- **Still to pin before 1B implementation starts:** `details.jsonl` record shape, dated-leaf naming convention (date format, dataset-slug derivation), metric-function unit-test surface.

**Worked 1B sweep example:**

```toml
dataset = "eval/datasets/wikipedia-v1.jsonl"
collection = "wikipedia-v1"

[matrix]
top_k = [10]
hybrid_alpha = [0.5]
```

---

## Phase 1C — Sweep matrix + per-query metrics across cells - COMPLETED

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

- `eval/datasets/wikipedia-v1.jsonl` (20–30 hand-curated queries
  with `gold_sources`).
- `eval/datasets/wikipedia-v1.README.md` (corpus URL, content
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

## Phase 1E — Icing: authoring helpers + answer-quality

Optional add-on, drafted after comparing Phase 1 against published RAG
reference implementations. None of these slices gate the Phase 1
Done-definition; they close gaps that the comparison exposed.

### 1E-α — `bench gen-queries`

**Deliverable:** a CLI verb that drafts candidate (query, `gold_sources`)
pairs from an indexed collection. Samples chunks, asks Claude to propose
queries answerable from those chunks, writes a `*.draft.jsonl` clearly
marked as unreviewed.

**Why this boundary:** the path from 5 → 30 hand-curated queries is the
tedious part of dataset authoring. LLM-drafted eval sets are common in
published RAG examples (often ~100 queries); we stop short of shipping
LLM-generated queries (brief non-goal) but reuse the scaffold to lower
the cost of getting to a reviewed set.

**Done when:** running the verb against an existing collection produces
a draft JSONL whose entries pass `bench validate-dataset` after manual
review and rename. The draft suffix is load-bearing — unreviewed files
must not be usable as eval inputs by accident.

---

### 1E-β — End-to-end answer accuracy (LLM-as-judge)

**Deliverable:** optional `[answer_eval]` block in the sweep TOML. When
enabled, for each (query × cell) the bench generates an answer from the
top-K payload, scores it against the dataset's `answer_text` with a
judge prompt, adds `answer_score` to `metrics.csv`, and adds
`answer_text` / `judge_rationale` to `details.jsonl`. Judge model id and
prompt version captured in `run.json` provenance.

**Why this boundary:** closes the brief's biggest acknowledged gap —
Phase 1 measures retrieval, not whether the answer is actually correct.
LLM-as-judge is a well-established pattern in the RAG-eval literature
and is known to be feasible at ~100-query scale. Pulling it in now also
forces the design question for Phase 4: either 1E-β _is_ the
expansion-eval metric (Phase 4 then just adds ±W cells to the sweep), or
Phase 4 layers section-coverage on top. Decision to be made before 1E-β
starts.

**Done when:** running a sweep with `[answer_eval]` enabled produces
per-cell mean `answer_score` numbers that pass a smell test against a
hand-judged sample of 5 queries.

---

### 1E-γ — Baseline-vs-current notebook cell

**Deliverable:** the Phase 1D starter notebook gains a "diff against
baseline" section: aggregate deltas, list of queries that flipped
hit↔miss, per-query rank changes. Takes two CSV paths; no engine
changes.

**Why this boundary:** the brief's discipline — "one change at a time,
always run the full eval set, per-query diffs are the primary debugging
surface" — needs an ergonomic surface or it won't be followed. The
grouped-bar variant-comparison visual common in RAG eval write-ups is
the right shape, adapted here to baseline-vs-current rather than
variant-vs-variant.

**Done when:** pointing the notebook at the committed Phase 0 baseline
CSV plus any later run's CSV produces a flip table and a delta plot
without notebook edits.

---

## Out of scope

- Phase 4 expansion-eval design (LLM-as-judge or section-coverage) —
  deferred per the brief's Open Questions. Note: 1E-β may absorb the
  LLM-as-judge candidate; decision recorded there.
- Bench CLI verbs beyond `run`, `validate-dataset`, and (if 1E-α lands)
  `gen-queries` — explicitly deferred.
- Personal-notes dataset and its baseline — parallel work, doesn't gate
  any of 1A–1D (or 1E).
