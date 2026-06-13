---
slug: phase-1-progress
title: Phase 1 — Eval Harness — Progress & Next Actions
status: active
parent: phase-1-spec.md
distilled_from:
  - archive/2026-05-18-eval-harness-implementation-phases.md
  - archive/2026-05-25-phase-1B-implementation-phases.md
  - archive/2026-06-10-claude-comments-about-1C-implementation.md
---

# Phase 1 — Eval Harness — Progress & Next Actions

The living "where we are". Decisions and the output contract are in
`phase-1-spec.md`. Phases are vertical slices, each producing something
observable.

## Status at a glance

| Slice | What it delivers | Status |
|---|---|---|
| 1A | Skeleton + `validate-dataset` | ✅ done |
| 1B | Single-cell `run` path, three output files | ✅ done |
| 1C | Full sweep matrix, per-(query×cell) metrics, stdout summary | ✅ done |
| — | FTS-returns-0-hits fix (gating, found by 1C) | ✅ applied — see `completed/2026-06-07-fts-simple-fix.md` |
| pre-1D | Versioning (`--version`, tags) + `collection_metadata`, before the Wikipedia ingest | ⏳ do first |
| 1D | Ship: public seed eval set, corpus README, committed baseline, starter notebook | ⏳ blocked on pre-1D |
| 1E-α | `gen-queries` authoring helper | ◻ optional |
| 1E-β | End-to-end answer accuracy (LLM-as-judge) | ◻ optional |
| 1E-γ | Baseline-vs-current notebook cell | ◻ optional |

## What is actually built

`src/Minerva.Search.Bench/`, assembly `minerva-bench`, hand-rolled arg
parser, verb-dispatch shell:

- **`validate-dataset <jsonl> --collection <name>`** — L3 validation:
  schema (required fields, unique `id`, `gold_sections` not populated, no
  unknown keys) + content lint + existence check of every `gold_sources`
  entry via `SourceIdExistsAsync` on `ISearchEngine`. Collect-all errors,
  plain-text report, exit 0/2/1.
- **`run --sweep <toml> --out <dir>`** — loads TOML (Tomlyn), enumerates the
  Cartesian product of `[matrix]`, runs `SearchPipeline.SearchAsync` per
  (query × cell), computes Recall@5/10/20 + MRR@10 in .NET, writes the dated
  result directory (`run.json`, `metrics.csv`, `details.jsonl`), prints a
  per-cell aggregate Recall summary.
- **`author-dataset --file <path> --collection <name>`** — the linear
  authoring helper (spec in `phase-1-spec.md`).
- **Versioning:** MSBuild target bakes `<SemVer>+<short-git-sha>` into
  `AssemblyInformationalVersion` for the bench (same pattern for Cli).

Tests: unit tests for parse + L3 logic, cell enumeration, and the metric
functions; integration tests in `Minerva.IntegrationTests` that seed a small
collection.

`eval/` currently holds (all private / gitignored): the
`personal-notes-v1` dataset, three per-collection sweeps
(`test-1` / `test-2` / `qwen2-5`), and the result runs from the first 1C
sweeps.

## 1C finding: the dataset is too easy and too small

The first real 1C sweeps (personal-notes-v1, 30 queries × 3 `hybrid_alpha`
× 3 collections) put recall near the ceiling, so the collections barely
separate and `hybrid_alpha` movement is hard to read. This is the eval doing
its job, but it means the current set cannot yet decide anything. The
remedies below are **acceptance criteria on 1D**, not a loose backlog — they
reshape the seed set that 1D ships.

## Before Phase 1D — pre-ingest fixed points (do first on resume)

Phase 1D ingests the Wikipedia corpus. That ingest is the right — and cheapest
— moment to bake collection provenance and settle versioning: there are only
throwaway test collections now, so we add the metadata, then re-ingest into
fresh, fully documented collections, with no retrofit onto live data later.
Because the provenance metadata carries version identifiers, versioning is
closed first. **1D is blocked on these two.**

### Versioning (low-hanging — close the noise)

- [ ] **`--version` flag (Gap A).** Expose the baked `InformationalVersion`
  (`<VersionPrefix>+<git-sha>`, already produced by the `StampGitSha` target in
  `Directory.Build.props`) as a `--version` flag on each shippable CLI:
  `Minerva.Search.Cli`, `Minerva.Search.Bench`, `Minerva.MarkdownIndexer`.
  Fixes minerva1's one weakness (its `--version` was hardcoded).
- [ ] **Release convention (Gap B).** Adopt `vX.Y.Z` git tags and a
  `CHANGELOG.md`. Lockstep single version is already achieved by
  `Directory.Build.props` (`VersionPrefix = 0.1.0`) — no bump script or
  consistency-guard test needed (minerva1 needed both). minerva2 versions
  independently of the Python minerva1 (0.1.0 vs 3.0.0).

### `collection_metadata` (bake before the ingest)

- [ ] A per-collection metadata row/table, written at ingest, holding:
  embedding model id+version, contextualization model + prompt version,
  ingestor commit SHA, chunker config (target size, overlap), DB schema
  version.
- [ ] Re-ingest the test collections and the new Wikipedia collection with the
  metadata populated, so every collection is self-describing from creation.
- [ ] Unblocks `run.json` tier-2 provenance in the bench (previously deferred)
  and is the data foundation for the ingestor reindex-policy decision on the
  roadmap.

### Contextualizer setting for the 1D ingest

Ingest the Wikipedia corpus with `qwen2.5` (the standing contextualization
model). `gemma4` is shelved — ruled out for bulk ingest on cost (the corpus is
~1.5× the personal-notes text, so gemma4 would be ~100–260 h versus qwen's
~day; no-context ~30 min). The eval measures relative deltas with the
contextualizer held fixed. Full model reconsideration is deferred to
`docs/future/backlog.md`.

## Phase 1D — Ship (next slice)

**Deliverable:** the four artifacts the Done-definition requires —

- `eval/datasets/wikipedia-top100-v1.jsonl` — 20–30 hand-curated queries
  with `gold_sources`.
- `eval/datasets/wikipedia-top100-v1.README.md` — corpus URL, content hash,
  license, ingestion command.
- `eval/results/phase0-baseline/` — the maintainer's committed run output.
- `eval/notebooks/phase0-analysis.ipynb` — loads the baseline CSV, produces
  the Recall@K table, a Recall-vs-HybridAlpha plot, and a per-query view.

**Dataset quality requirements (from the 1C finding) — the seed set must
include:**

- [ ] **Keyword-only / lexical-favouring queries** — codes and acronyms
  where dense retrieval likely misses and the FTS branch should win:
  `LRGB`, `IRPEF`, `BARNARD 22`, `Hα`. **Most important omission:** without
  these the eval is blind to the lexical branch the FTS fix just repaired,
  so it cannot demonstrate that hybrid adds anything over pure vector.
- [ ] **Harder semantic queries** — concept-expressed-differently (paraphrase
  that does not mirror the document vocabulary) and multi-gold queries, so
  recall comes off the ceiling and collections can separate.
- [ ] **Enough volume** — grow past the current ~30 toward the 20–30 _public_
  hand-curated target; the set is currently too small to decide.
- [ ] **Scoring depth** — bump so that Recall@20 carries real signal rather
  than saturating.

**Done when:** nuking local state, following the corpus README, and
re-running the bench produces output the committed notebook analyzes without
edits.

## Phase 1E — Optional add-ons (icing)

None gate the Phase 1 Done-definition.

- **1E-α `gen-queries`** — a verb that drafts candidate (query,
  `gold_sources`) pairs from an indexed collection, writing a
  `*.draft.jsonl` clearly marked unreviewed. Lowers the cost of getting to a
  reviewed set. The `.draft` suffix is load-bearing — unreviewed files must
  not be usable as eval input by accident.
- **1E-β answer accuracy (LLM-as-judge)** — optional `[answer_eval]` block in
  the sweep TOML; generates an answer from the top-K payload, scores it
  against `answer_text`, adds `answer_score` to `metrics.csv` and judge
  fields to `details.jsonl`; judge model id + prompt version captured in
  `run.json`. This forces a Phase 4 decision: either 1E-β _is_ the
  expansion-eval metric (Phase 4 just adds ±W cells), or Phase 4 layers
  section-coverage on top. Decide before 1E-β starts.
- **1E-γ baseline-vs-current notebook cell** — "diff against baseline":
  aggregate deltas, queries that flipped hit↔miss, per-query rank changes.
  Takes two CSV paths, no engine changes. **This is where the per-query-diff
  method (in `phase-1-spec.md`) becomes ergonomic** — turn "read the diffs"
  into a standing report: with n≈30 near the ceiling, list which individual
  queries each collection misses, not just averages.

## Out of scope for Phase 1

- Phase 4 expansion-eval design (LLM-as-judge or section-coverage) — deferred;
  1E-β may absorb the LLM-as-judge candidate.
- Bench verbs beyond `run`, `validate-dataset`, `author-dataset`, and (if
  1E-α lands) `gen-queries`.
- Personal-notes dataset and its baseline — parallel work, gates no slice.
