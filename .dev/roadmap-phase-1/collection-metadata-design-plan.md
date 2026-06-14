# Collection Metadata & Provenance — Build Plan

A high-level journal for the work designed in
[.dev/roadmap-phase-1/collection-metadata-design.md](collection-metadata-design.md).
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

- `[~]` **A — Schema + typed model round-trip.** New migration drops the two
  embedding columns; `Metadata` becomes the two-section structure; repository
  (de)serializes it. No behaviour change. *Production done; verify pending.*
  *Verify:* unit test round-trips a full provenance object through create/get.
- `[~]` **B — Prompt versioning + populate at ingest.** Version constants on the
  two ingestion leaves; engine assembles invariants + last-run and writes
  provenance on create. *Production done; verify pending.*
  *Verify:* integration test ingests fresh, reads back, asserts every field.
- `[~]` **C — Enforcement.** Generalize the mismatch exception to list every
  drifted field; compare the whole invariant set on reingest; rename the
  recreate-override flag across option record, binder, engine interface,
  indexer, and tests. *Production done; verify pending.*
  *Verify:* integration test — changed chunk size throws; override recreates.
- `[ ]` **D — Bench tier-2 provenance.** The run-json writer fetches the
  collection under test and stamps its provenance into `run.json`.
  *Verify:* a sweep produces `run.json` carrying the provenance block.
- `[ ]` **E — Indexer client metadata + soft path guard.** Indexer writes its
  `client` section and, on reingest, applies its own policy (`kind` critical,
  `sourceRoot` overridable, globs free).
  *Verify:* changed source root warns/blocks; override proceeds.
- `[ ]` **F - Manual run** Perform a debug step by step run of ingestion and update to verify every step manually

## Decisions deferred to their slice

- **(C)** ~~Exact name of the generalized exception and the renamed flag.~~
  Resolved: `CollectionConfigMismatchException` and `AllowRecreateOnConfigMismatch`.
- **(future, no slice)** Where the drift-recovery policy lives — core (as built:
  guards *and* recreates, gated by the flag) vs front-end (core pure always-throw
  guard, each indexer catches and decides). Parked in `docs/future/backlog.md`;
  does not affect the collection format.
- **(E)** Whether the indexer's path guard reuses the core override flag or adds
  its own. Proposed default: a separate `AllowSourceRootChange`, mirroring the
  existing opt-in pattern.

## Current status

A/B/C implemented in production; all `src` projects compile. Tests are broken —
they still reference the old `Collection` constructor, the old create/ensure
signatures, the removed `CollectionEmbedderMismatchException`, and the old flag
name. Next action: repair the tests and write the A/B/C verifies, then D.
