# Minerva — single-options surface conflates ingest and search

## Observation

`MinervaOptions` today is a single root that hosts two unrelated workloads:

- **Ingestion**: needs `Embedding`, `Llm` (when summarization/contextualization
  is enabled), `Chunking`, and a database `ConnectionString`.
- **Search**: needs `Embedding` and `ConnectionString`. Does not need `Chunking`
  or `Llm`.

`Minerva.Search.Cli` proves the asymmetry: it instantiates a Minerva engine to
issue queries but has no business reasoning about chunk size, overlap, or
chunker type. Its `appsettings.json` either pads a `Chunking` section it never
exercises, or — more honestly — should not have to know about it at all.

This is a violation of the single-responsibility principle at the *options*
boundary. The engine type itself (`MinervaEngine`) does both jobs because the
underlying repository and embedding provider are shared, but the *configuration
contract* should not pretend the two are one feature.

## Symptom that surfaced this

While refactoring options to strict immutable records (see
`2026-05-09-strict-options-records.md`), the question came up: should
`MinervaOptions.Chunking` be `required` or nullable?

Both answers are bad:

- **Required**: Search.Cli must pad its config with chunking values it never
  uses — strictness contagion.
- **Nullable**: Ingestion paths must guard against null at every chunking-aware
  call site — convenience contagion in the opposite direction.

The right fix is structural, not a property nullability tweak.

## Suggested direction (for future work)

Split the engine surface along the responsibility line:

- `MinervaSearchOptions { ConnectionString, Embedding }`
- `MinervaIngestOptions { ConnectionString, Embedding, Chunking, Llm? }`

A third "engine root" can compose them when a host genuinely needs both, e.g.
the indexer:

- `MinervaEngineOptions { Search: MinervaSearchOptions, Ingest:
  MinervaIngestOptions }`

Hosts that do only one thing (Search.Cli) bind only their corresponding
record. The engine factory accepts whichever shape the host provides and only
constructs the components it can.

This also exposes a likely engine-level split: an `ISearchEngine` and an
`IIngestEngine` interface, with a thin facade for hosts that need both. The
shared infrastructure (data source, embedding provider) is wired once at
composition, not duplicated.

## Out of scope right now

The strict-options refactor will, as a temporary measure, pad
`Search.Cli/appsettings.json` with the keys that `MinervaOptions.Chunking`
requires, so the immediate refactor lands as a single PR. This file marks the
underlying design problem so it is not lost.

## Status

Deferred — log only. Pick up after the strict-options refactor is in.
