# Collection Metadata & Provenance — Build Plan

A high-level journal for the work designed in
[.dev/roadmap-phase-1/collection-metadata-design.md](2026-06-18-collection-metadata-design.md).
Prose only — no types, signatures, or code shapes. The design doc is the
specification; this file tracks progress.

## Goal

Make every collection self-describing and drift-proof before the Wikipedia
ingest. The `collections.metadata` column becomes a two-section object: a typed
`provenance` block owned and guarded by Minerva core, and an opaque `client`
block owned by the front-end. Re-ingesting a collection with a changed content
determinant (model, chunk size, prompt version, …) becomes a hard error instead
of silently producing an incoherent index.

## Chosen design

- **Two owners.** Core provenance is typed and guarded; `client` is an opaque
  blob core never interprets. Keeps core uncoupled from front-end concepts.
- **Three field classes.** *Invariant* (defines the stored bytes — guarded,
  drift is a hard error), *last-run* (recorded, refreshed each ingest, never
  guarded), *excluded* (secrets, infrastructure, throughput knobs — never
  stored).
- **Prompts become invariants by versioning.** A hardcoded prompt is an
  invisible content determinant; a co-located version constant makes it
  guardable. Other code-level determinants are pinned by the recorded ingestor
  git SHA, not individually versioned.

## Working principle

Vertical slices, not horizontal layers. Each slice cuts through every layer it
needs, compiles, and ends with the build and tests green. Types and shapes are
born in the slice that first needs them, grown later when a slice needs more —
not designed up front.

## Slices

Legend: `[ ]` not started · `[~]` in progress · `[x]` done.
Core line A → B → C → D; E hangs off A and does not block the Wikipedia ingest.

- `[x]` **A — Schema + typed model round-trip.** New migration drops the two
  embedding columns; `Metadata` becomes the two-section structure; repository
  (de)serializes it. No behaviour change.
  *Verify:* `CollectionRepositoryTests` round-trips a full provenance object
  (and the contextualization-disabled case omits the optional fields).
- `[x]` **B — Prompt versioning + populate at ingest.** Version constants on the
  two ingestion leaves; engine assembles invariants + last-run and writes
  provenance on create.
  *Verify:* `CollectionProvenanceE2ETests.Ingest_PopulatesProvenance` ingests
  fresh, reads back, asserts every populated field.
- `[x]` **C — Enforcement.** Generalize the mismatch exception to list every
  drifted field; compare the whole invariant set on reingest; rename the
  recreate-override flag across option record, binder, engine interface,
  indexer, and tests.
  *Verify:* `CollectionProvenanceE2ETests` — changed chunk size throws naming
  the drifted field; the override flag recreates.
- `[x]` **D — Bench tier-2 provenance.** The run-json writer fetches the
  collection under test and stamps its provenance into `run.json`.
  *Verify:* a sweep produces `run.json` carrying the provenance block.
  Confirmed by a manual sweep against `mynotes-nollm`.
- `[x]` **E — Indexer client metadata + soft path guard.** Indexer writes its
  `client` section and, on reingest, applies its own policy (`kind` critical,
  `sourceRoot` overridable, globs free, missing/foreign provenance rejected).
  `ValidateNoLiteralApiKeys` re-wired to guard the client `data`, and its two
  tests restored (`*ClientDataContainsLiteralApiKey*`, `*ClientDataWithNonKeyStrings*`).
  The `client` section is a typed `ClientProvenance(kind, data)` record: core
  mandates `kind` (envelope contract) and treats `data` as opaque. A read-time
  boundary guard in `ReadCollection` rejects incomplete metadata (a collection
  ingested before this slice has no client section and now fails fast on read).
  *Verify:* the literal-API-key rejection is covered by unit tests. The
  source-root automated verifies (changed root blocks; override proceeds) are
  **deferred to the backlog** — they need an indexer test project; see
  `docs/future/backlog.md`. Guard behaviour confirmed manually.
- `[~]` **F — Manual run.** Debug step-by-step run to verify every stage.
  Bench search path + `run.json` provenance confirmed against `mynotes-nollm`.
  Remaining: reingest `mynotes-nollm` under the slice-E indexer so it carries a
  client section (it predates E, so the read-boundary guard now rejects it).

## Decisions deferred to their slice

- **(C)** ~~Exact name of the generalized exception and the renamed flag.~~
  Resolved: `CollectionConfigMismatchException` and `AllowRecreateOnConfigMismatch`.
- **(future, no slice)** Where the drift-recovery policy lives — core (as built:
  guards *and* recreates, gated by the flag) vs front-end (core pure always-throw
  guard, each indexer catches and decides). Parked in `docs/future/backlog.md`;
  does not affect the collection format.
- **(E)** ~~Whether the indexer's path guard reuses the core override flag or adds
  its own.~~ Resolved: a separate `AllowSourceRootChange` flag, mirroring
  `AllowRecreateOnConfigMismatch` (validated for presence in the indexer binder).
- **(future, no slice)** Client-envelope schema versioning — stamp a `version`
  before deploy, defer the handler. Recorded in `docs/future/backlog.md`.

## Current status

A–E complete: whole solution builds, 253 tests pass (220 unit, 22 integration,
11 architecture). D verified by a manual sweep; E's literal-API-key verify is
green, its source-root verifies are deferred to the backlog (no indexer test
project yet). Slice F is in progress — bench search and `run.json` provenance
confirmed; the only remaining step is the manual reingest of `mynotes-nollm`
(a runtime action, not a code change).

Three pre-deploy follow-ups are parked in `docs/future/backlog.md`: a
`MarkdownIndexerException`/`CollectionMetadataException` family for clean error
reporting, the indexer source-root guard tests, and the client-envelope
`version` stamp.
