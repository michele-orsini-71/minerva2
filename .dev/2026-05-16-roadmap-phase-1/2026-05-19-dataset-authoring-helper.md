---
slug: 2026-05-19-dataset-authoring-helper
created: 2026-05-19T19:46:03Z
last_updated: 2026-05-24T00:00:00Z
status: finalized
---

# Dataset Authoring Helper

## Goal

A small one-shot helper, shipped as a subcommand of `Minerva.Search.Bench`,
that lets the developer author eval dataset entries (JSONL) for a single
source per run. Workflow: pick one collection and one output file at launch,
enter a search query, pick a source by rank from the displayed results, then
type one or more queries that should retrieve it. All entries are written to
the dataset file at the end and the process exits. The tool removes the
friction of consulting the DB manually and hand-crafting JSON.

## Constraints and Non-Goals

- One collection per process run; no mid-session switch.
- One output file per process run.
- One source per process run; to author entries for another source, relaunch.
- One gold source per entry in v1 (multi-source entries are only authorable by
  hand-editing the JSONL).
- No support for `notes` or `answer_text` in v1; users can add them by
  editing the JSONL afterwards.
- `gold_sections` is reserved for Phase 4 — the tool never writes it.
- Not a generic DB browser. Search uses the same Minerva pipeline that the
  dataset will eventually be evaluated against.
- No REPL, no commands, no `:undo`. The flow is linear; corrections happen
  by re-running the tool (and hand-editing the JSONL when needed).

## Decisions

### Shape and entry point

**Choice**: New subcommand under `Minerva.Search.Bench`, linear prompt flow.
**Rationale**: The dataset format, validator, and DB wiring already live in
Bench; co-locating keeps DI and configuration trivial. A linear flow (as
opposed to a REPL) keeps the code surface tiny while still covering the
"pick one source, write several queries against it" rhythm.

### CLI surface

**Choice**: Both the dataset file and the collection are required CLI args.
**Rationale**: Both are session-wide and don't need to be switchable
mid-session. Indicative shape:

```
Minerva.Search.Bench author-dataset --file <path> --collection <name>
```

### Output file lifecycle

**Choice**: At startup, if the file exists, parse it to seed seen-IDs and
per-source index counters. During the session, hold the new entries in memory.
On exit (after the question loop ends with an empty line), append all new
entries to the file in one write.
**Rationale**: Buffering in memory keeps the implementation linear and gives
the developer an implicit "undo" — abandoning the run (Ctrl-C) before the
final write discards any typos. The session is short enough that holding
entries in memory is harmless. Parsing existing entries on startup keeps ID
generation collision-free across runs.

### Behavior on malformed existing JSONL at startup

**Choice**: Abort with a clear error pointing to the offending line.
**Rationale**: Continuing on a malformed file would silently desync ID
counters and may hide a real problem. Forcing the developer to remediate
manually is safer for a small, hand-curated dataset.

### Linear prompt flow

**Choice**: The tool prompts in this fixed sequence:

1. Search query → run `ISearchEngine.SearchAsync`, dedupe by `source-id`
   keeping the best-scoring chunk per source, display as
   `[rank] source-id score excerpt(~120 chars)`. Default top-N = 10.
2. Rank of the source to author for → resolve to a `source-id`. Reject
   out-of-range values; on rejection, exit (no retry loop in v1).
3. Question loop: prompt for a question; each non-empty line is buffered as
   a new entry `{id, query, gold_sources:[<source-id>]}`. An empty line ends
   the loop.
4. Write all buffered entries to the file in one append, then exit.

**Rationale**: The developer's typical flow is "one source, several queries
(English keyword, Italian semantic, curve-ball)". A linear pick + question
loop captures that rhythm without the state-machine surface of a REPL.
Source-level dedupe matches the unit of authoring (`gold_sources` is a
source-id list). Using the full pipeline ensures queries are meaningful
relative to the production behaviour the dataset will be evaluated against.

### ID generation

**Choice**: Auto-generate ids as `<source-id>-<n>`, where `n` is the next
1-based index for that source within the dataset file. At startup, seed the
counter for each source by taking `max(n)` over existing ids matching the
exact pattern `^<source-id>-(\d+)$`; ids that don't match the pattern are
ignored for counter seeding but still tracked in the seen-ids set for
uniqueness. If a freshly generated id collides with an existing one
(possible when hand-edited ids are interleaved beyond the current max),
bump `n` and retry until free.
**Rationale**: Keeps ids deterministic, traceable, and grouped — when reading
the JSONL later you can see "all queries for source X" at a glance. Tolerates
hand-edited entries without losing the uniqueness guarantee; gaps in the
sequence (e.g. skipping from `-2` to `-5`) are harmless.

## Approach Preferences

- Reuse the existing dataset schema and validator (`DatasetValidator`,
  `ParsedEntry`). The tool emits JSONL that the existing validator accepts.
- Reuse `MinervaSearchEngine` / `ISearchEngine` for the search step. No
  retrieve-only shortcut.
- No special handling for concurrent runs — the developer launches one
  instance at a time per file.

## Open Questions

- [X] Should the tool print a short session summary (entries added, source
      authored) before exiting? Cosmetic, can be deferred.
      Answer: YES
- [X] Naming of the subcommand verb (`author-dataset`, `compose-dataset`,
      `dataset-author`, …) — bikeshed during implementation.
      Answer: author-dataset is fine

## Research Findings

- **Existing dataset schema** (`src/Minerva.Search.Bench/Validation/DatasetValidator.cs`):
  JSONL per line with required `id` (unique string), `query` (non-empty
  string), `gold_sources` (non-empty array of strings). Optional `notes` and
  `answer_text`. `gold_sections` is reserved for Phase 4 and must not be
  populated. The helper writes only `id`, `query`, and `gold_sources` — never
  `gold_sections`, `notes`, or `answer_text`.
