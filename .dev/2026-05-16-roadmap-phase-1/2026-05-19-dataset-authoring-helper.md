---
slug: 2026-05-19-dataset-authoring-helper
created: 2026-05-19T19:46:03Z
last_updated: 2026-05-20T05:36:25Z
status: finalized
---

# Dataset Authoring Helper

## Goal

A small REPL helper, shipped as a subcommand of `Minerva.Search.Bench`, that
lets the developer author an eval dataset (JSONL) by browsing a chosen
collection in the DB. Workflow: pick one collection and one output file at
launch, search the collection to locate a source, `:pick` that source, then
type one or more queries that should retrieve it. Each query is committed
immediately as a valid JSONL line in the dataset file. The tool removes the
friction of consulting the DB manually and hand-crafting JSON.

## Constraints and Non-Goals

- One collection per process run; no mid-session switch.
- One output file per process run; no `:open` / `:close` commands.
- One gold source per entry in v1 (multi-source entries are only authorable by
  hand-editing the JSONL).
- No REPL support for `notes` or `answer_text` in v1; users can add them by
  editing the JSONL afterwards.
- `gold_sections` is reserved for Phase 4 — the tool never writes it.
- Not a generic DB browser. Search uses the same Minerva pipeline that the
  dataset will eventually be evaluated against.

## Decisions

### Shape and entry point

**Choice**: New subcommand under `Minerva.Search.Bench`, REPL-style.
**Rationale**: The dataset format, validator, and DB wiring already live in
Bench; co-locating keeps DI and configuration trivial.

### CLI surface

**Choice**: Both the dataset file and the collection are required CLI args.
**Rationale**: Both are session-wide and don't need to be switchable
mid-session — relaunching the tool is cheaper than carrying REPL state for a
switch. Indicative shape:

```
Minerva.Search.Bench author-dataset --file <path> --collection <name>
```

### Output file lifecycle

**Choice**: Open the JSONL file at startup; append on every committed entry.
If the file exists, parse it to seed seen IDs and per-source index counters.
**Rationale**: Append-on-commit means no "save" step and no risk of losing
work mid-session. Parsing existing entries keeps ID generation collision-free
across runs.

### Behavior on malformed existing JSONL at startup

**Choice**: Abort with a clear error pointing to the offending line.
**Rationale**: Continuing on a malformed file would silently desync ID
counters and may hide a real problem. Forcing the developer to remediate
manually is safer for a small, hand-curated dataset.

### Search command

**Choice**: `:search <query>` shows top-N hits as
`[rank] source-id score excerpt(~120 chars)`. Default `N = 10`, widen with
`--n <int>`. Rows are collapsed to one per `source-id`, keeping the
best-scoring chunk's excerpt; `--n` therefore counts distinct sources, not
chunks. To return N distinct sources, the implementation over-fetches chunks
from `ISearchEngine.SearchAsync` and dedupes by `source-id` before display.
Uses the full Minerva pipeline as currently implemented (vector + FTS hybrid
with rank fusion).
**Rationale**: The dataset will be evaluated against the full pipeline, so
authoring against the same pipeline ensures queries are meaningful relative to
the production behaviour. Source-id + short excerpt is the minimum surface
needed to confirm a hit and grab the id to `:pick`. Chunk-level rows would
faithfully mirror what the engine returns but produce noisy output when a
long source dominates the top hits; since the dataset records `gold_sources`
(source-level), the deduped view matches the unit of authoring.

### Pick / compose mode

**Choice**: `:pick <source-id | rank>` enters compose mode anchored to that
source. The argument is either a literal source-id or a rank index from the
most recent `:search` output (e.g. `:pick 3` = the 3rd row). Rank-form is
rejected if no `:search` has run in this session. In compose mode, every
typed line becomes a new JSONL entry `{id, query, gold_sources:[<source-id>]}`,
appended immediately. An empty line exits compose mode back to the top-level
REPL. Lines starting with `:` inside compose mode are interpreted as commands
(e.g. `:undo`, `:quit`); queries cannot start with `:`, which is acceptable
in practice.
**Rationale**: The developer's typical flow is "one source, several queries
(English keyword, Italian semantic, curve-ball)". Compose mode collapses that
into the natural one-line-per-query rhythm without per-line ceremony.
Accepting a rank index avoids retyping long source-ids that were just
displayed on screen.

### Behavior when `:pick` source-id is not in the chosen collection

**Choice**: Friendly error; do not enter compose mode.
**Rationale**: A typo in the source-id would otherwise produce JSONL entries
that fail validation later. Failing loud at `:pick` keeps the dataset clean.

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

### Undo

**Choice**: `:undo` pops the most recent line from the dataset file
(truncating at the previous newline) and re-derives per-source counters from
the remaining lines. It refuses to truncate below the byte offset captured
when the session opened the file, so pre-session entries are never destroyed.
Compose mode and top level both accept it. Newline convention is LF.
**Rationale**: Without undo a single typo permanently pollutes the dataset.
Truncate-to-previous-newline is the simplest implementation that matches the
append-only commit model. Re-deriving counters from the remaining lines keeps
ID generation consistent across cross-source undo sequences; the byte-offset
guard ensures the tool can't eat work that wasn't authored in this session.

## Approach Preferences

- Reuse the existing dataset schema and validator (`DatasetValidator`,
  `ParsedEntry`). The tool emits JSONL that the existing validator accepts.
- Reuse `MinervaSearchEngine` / `ISearchEngine` for `:search`. No
  retrieve-only shortcut.
- Keep the REPL surface tiny: `:search`, `:pick`, `:undo`, `:quit` (or
  Ctrl-D). Nothing else in v1.
- No special handling for concurrent runs — the developer launches one
  instance at a time per file.

## Open Questions

- [ ] Should `:quit` (or EOF) print a short session summary (entries added,
      sources touched) before exiting? Cosmetic, can be deferred.
- [ ] Naming of the subcommand verb (`author-dataset`, `compose-dataset`,
      `dataset-author`, …) — bikeshed during implementation.

## Research Findings

- **Existing dataset schema** (`src/Minerva.Search.Bench/Validation/DatasetValidator.cs`):
  JSONL per line with required `id` (unique string), `query` (non-empty
  string), `gold_sources` (non-empty array of strings). Optional `notes` and
  `answer_text`. `gold_sections` is reserved for Phase 4 and must not be
  populated. The helper writes only `id`, `query`, and `gold_sources` — never
  `gold_sections`, `notes`, or `answer_text`.
