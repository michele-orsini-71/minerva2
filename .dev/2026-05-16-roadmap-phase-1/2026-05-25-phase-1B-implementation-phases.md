---
slug: 2026-05-25-phase-1B-implementation-steps
created: 2026-05-25T00:00:00Z
status: draft
parent: 2026-05-18-eval-harness-implementation-phases.md
---

# Phase 1B — Single-cell run path: implementation steps

Baby-step breakdown of Phase 1B. Each step is independently verifiable.
"TBD" markers flag micro-decisions to be made when arriving at that step,
not now.

See parent for Phase 1B's resolved decisions (CLI signature, sweep TOML
shape, `run.json` tier-1 fields, bench versioning approach, deferred
items).

## Sub-actions

### 1. Bench versioning MSBuild target

Add an MSBuild target to `Minerva.Search.Bench.csproj` that runs
`git rev-parse --short HEAD` and writes
`AssemblyInformationalVersion = "<SemVer>+<sha>"`. Apply the same target
to `Minerva.Search.Cli` (and to the ingestor when convenient). Expose a
runtime read via `Assembly.GetExecutingAssembly()
.GetCustomAttribute<AssemblyInformationalVersionAttribute>()`.

**Verify:** `minerva-bench --version` (or equivalent debug hook) prints
`0.1.0+abc1234`.

### 2. `run` verb dispatch in the arg parser

Extend the verb-dispatch shell from 1A to recognize `run`. Define a typed
args record with `--sweep <path>` and `--out <dir>`, both required.
Update `PrintUsage`.

**Verify:** `minerva-bench run` with missing args prints usage and exits
non-zero; with valid args, parses to the typed record (no-op handler).

### 3. Sweep TOML model + loader

Pick a TOML lib (Tomlyn is the .NET standard: MIT, minimal). Define a
`SweepConfig` record: `string Dataset`, `string Collection`,
`IReadOnlyDictionary<string, IReadOnlyList<object>> Matrix`. Loader is a
pure function `string → SweepConfig | List<Error>` per 1A's collect-all
convention. Validate: dataset and collection present and non-empty,
`[matrix]` present with ≥1 knob, every knob a non-empty list, knob name
in the allow-list (`top_k`, `hybrid_alpha` for 1B).

**Verify:** unit tests against good and malformed TOML strings; every
failure mode produces a clear message.

### 4. Cell enumeration

Pure function `SweepConfig.Matrix → IReadOnlyList<Cell>` where `Cell` is
an ordered dictionary of knob → scalar value. Cartesian product over the
matrix's value lists.

**Verify:** unit tests for 1×1, 1×3, 2×3, and that knob order in the
output `Cell` is stable (matters for `metrics.csv` column order
downstream).

### 5. Metric functions (pure)

Signature: `(IReadOnlyList<RetrievedChunk> topK,
IReadOnlySet<string> goldSources) → (Recall@5, Recall@10, Recall@20,
MRR@10)`. Document-level: hit iff any chunk's `source_id` is in
`goldSources`. MRR@10 = reciprocal rank of first gold-source hit within
top-10, else 0.

**Verify:** unit tests covering no hit, hit at rank 1/5/10/11, multiple
gold sources, top-K shorter than 20. Write these tests before step 6
(unit-test uncertain algorithmic components first).

### 6. Run loop

For each cell × each dataset entry: build per-call search options from
the cell's knob values, call `SearchPipeline.SearchAsync`, stopwatch the
call, run the metric functions, accumulate a per-(query × cell) result
record. Per-query errors are caught and recorded as a metric row with
null metrics + an error column (TBD column name at step 9), never abort
the run.

**Verify:** a small in-process run against a fake `ISearchEngine`
returns the expected number of result records.

### 7. Output directory minting

On first write, create `<out>/<timestamp>_<dataset-slug>/`.

**TBD when implementing:** timestamp format (`2026-05-25T14-30-22` vs
alternatives), dataset-slug derivation (filename without `.jsonl`?
slugified?), collision policy (suffix `_2` if exists? error?).

**Verify:** directory exists with the expected name pattern; two
back-to-back invocations do not collide.

### 8. `run.json` writer

Serialize the tier-1 fields: `timestamp`, `bench_version`,
`dataset_path`, `collection`, `resolved_sweep`, `cells`.

**TBD when implementing:** exact JSON shape for `resolved_sweep`
(re-emit as TOML-equivalent JSON?) and `cells` (list of objects).

**Verify:** file is valid JSON; `System.Text.Json` round-trips it.

### 9. `metrics.csv` writer

Headers: `query_id`, one column per knob in stable order,
`recall_at_5`, `recall_at_10`, `recall_at_20`, `mrr_at_10`,
`latency_ms`, `error`. One row per (query × cell). Use a real CSV
writer (CsvHelper) or hand-roll if column count stays small.

**Verify:** `pd.read_csv()` opens it without dtype warnings; row count =
queries × cells.

### 10. `details.jsonl` writer

One JSON object per line, one line per (query × cell).

**TBD when implementing:** record shape — chunk identifier fields, full
chunk text vs IDs only, per-stage intermediate results (semantic / BM25
/ RRF survivors), gold-hit annotation, per-stage latency. Tradeoffs in
plan's Open Question #4.

**Verify:** every line parses with `json.loads`; line count = queries ×
cells.

### 11. Integration test

End-to-end test in `Minerva.IntegrationTests` mirroring 1A: seed a small
collection, point bench at a 3-query JSONL, assert exit 0, all three
output files exist, `metrics.csv` has 3 rows, `run.json` has the
expected `collection` value.

### 12. Manual smoke / Done-when verification

Hand-write a 5-query JSONL against an existing local collection, run
end-to-end, open `metrics.csv` in a Jupyter notebook with pandas,
eyeball that Recall numbers look plausible. This is the Done-when from
the parent phases doc.

## Dependency notes

- 1, 2 are independent and can run in parallel.
- 3 → 4 → 6.
- 5 is independent until 6.
- 7, 8, 9, 10 depend on 6 producing result records.
- 11 depends on everything; 12 is human-in-the-loop and final.

## Deferred to other phases

- `eval/sweeps/private/` and `eval/datasets/private/` gitignore entries
  → Phase 1D, when actual sweep files land.
- Aggregate Recall@K stdout summary per cell → Phase 1C.
- `collection_metadata` row in Postgres (embedding model id+version,
  ingestor commit SHA, DB schema version) → sibling task between 1C
  and 1D; unblocks `run.json` tier-2 fields.
- Ingestor versioning / reindex policy → main roadmap, after Phase 1.
