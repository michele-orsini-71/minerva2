---
slug: 2026-05-09-strict-options-records
created: 2026-05-09T08:13:27Z
last_updated: 2026-05-09T08:49:22Z
status: finalized
---

# Strict Options as Immutable Records

## Goal

Refactor option types from mutable, binder-shaped POCOs to immutable records
that are the single source of truth for option contracts. Validation moves to
construction-time factories in an outer layer; core records carry zero
framework coupling. While doing the refactor, repair structural mistakes
exposed by the strictness audit:

- Drop `EnableSummarization` / `EnableContextualization` flags. Move the LLM
  provider under `ChunkingOptions` so its presence is the opt-in for
  summarization + contextualization (the only places that consume an LLM).
- Split the shared `ProviderOptions` into `EmbeddingProviderOptions` and
  `LlmProviderOptions` so `BatchSize` (real for embedding, dead for LLM) only
  exists where it is used.

The current shape exists for the convenience of `IConfiguration` binding
(mutable setters, parameterless ctor, optional types where mandatory was
required, convenience defaults). That convenience has corrupted the contract.

## Constraints and Non-Goals

- Out of scope: `SearchOptions` — per-call query record, not startup
  configuration. Defaults defensible.
- Out of scope: `ConfigurationException` and code that throws it
  (`CredentialResolver`, `CollectionManager`, `MinervaEngine`). Unrelated to
  option shape.
- Out of scope: `ProviderFactory` ApiKey handling. Current
  resolver-or-fallback behavior stays.
- Out of scope: splitting Minerva into separate search and ingest engines.
  The `MinervaOptions.Chunking` requirement currently leaks into Search.Cli;
  workaround = pad its `appsettings.json`. Underlying design problem is
  captured in `minerva-search-options.md` for follow-up.
- No XML doc comments on options or DTOs.
- Big-bang migration in one PR. Incremental migration is rejected as more
  churn than it saves.

## Decisions

### Record style

**Choice**: `record class` with `required` `init`-only properties. No
positional syntax. Collection-typed properties use `IReadOnlyList<T>` and the
factory copies the input into an `ImmutableArray<T>` (or equivalent) so a
caller mutating the source list does not mutate the record.
**Rationale**: Immutability + value equality + clear property syntax. `record
class` over `record struct` for reference identity (DI flow stays clean) and
because nullable `record class` provides the `null` sentinel for
`ChunkingOptions.Llm`.

### Type structure

**Choice**:

- `MinervaOptions { ConnectionString, Embedding: EmbeddingProviderOptions,
  Chunking: ChunkingOptions }` — no top-level `Llm`.
- `EmbeddingProviderOptions { BaseUrl, Model, ApiKey?, Concurrency, BatchSize,
  RequestsPerMinute? }`
- `LlmProviderOptions { BaseUrl, Model, ApiKey?, Concurrency,
  RequestsPerMinute? }` — no `BatchSize`.
- `ChunkingOptions { TargetChunkSize, ChunkOverlap, MaxSegmentChars,
  ChunkerType, Llm: LlmProviderOptions? }` — no flags.
- `IndexerOptions { RootPath, CollectionName, ExcludeDirectories }` — no
  `FilePattern`. The scanner inlines `"*.md"` as a `const` since this is the
  *Markdown* indexer.

**Rationale**:

- `ILlmClient` is consumed only by `DocumentSummarizer` and
  `ChunkContextualizer`. Nesting the LLM provider inside `ChunkingOptions`
  makes the conditionality structural: present = augmentation enabled, absent
  = neither feature runs. Eliminates the silent-fallback class of bugs
  (`EnableSummarization && Llm == null`).
- `BatchSize` is read by `EmbeddingService` for embedding, never for LLM.
  Splitting the provider type stops dead config (e.g. `BatchSize: 1` on the
  current `Llm` section in `appsettings.debug.json`) from sitting in JSON
  pretending to mean something.
- `FilePattern` was the lone code-default carve-out in the prior draft;
  dropping the property and inlining `"*.md"` keeps the "code is default-free"
  rule unbroken.

### Validation pattern — factory contract

**Choice**:

- Each record exposes a public static `Bind(IConfiguration cfg)` (or
  equivalent JSON-rooted entry) that returns a validated record or throws
  `OptionsValidationException`.
- Internally each record exposes an `internal static TRecord? TryBuild(TRaw
  raw, string stagePrefix, List<OptionsFailure> failures)` primitive used by
  parent factories to compose validation. Children append to the shared
  `failures` list and return `null` on failure; siblings continue —
  **no early termination across siblings or children**.
- Top-level `Bind` calls `TryBuild`, then throws once with the full failure
  list if any were collected.
- Path format in `OptionsFailure.Path`: `Section.Property`, e.g.
  `Embedding.BaseUrl`, `Chunking.Llm.Model`. The `stagePrefix` argument is
  the dotted path so far (with trailing `.`); leaf checks append the property
  name.

**Rationale**: One pass surfaces every problem at once. Public surface is
JSON-rooted so tests never need to construct raw DTOs. The `TryBuild`
primitive stays internal because it is composition machinery, not part of the
contract.

### Exception type for option failures

**Choice**: New types in `Minerva.Configuration`:

```csharp
public sealed record OptionsFailure(string Path, string Reason, Exception? Cause = null);

public sealed class OptionsValidationException : MinervaException
{
    public IReadOnlyList<OptionsFailure> Failures { get; }
    // Message: deterministic, "<count> option(s) invalid:\n  - <Path>: <Reason>\n..."
}
```

Failures are deterministic in order (insertion order from depth-first
traversal). `OptionsFailure` is kept distinct from `PreflightFailure` despite
identical shape — option-shape errors are static; preflight errors are
environmental.

### Factory location

**Choice**: Public binders and internal raw DTOs live in the
`Minerva.Configuration` namespace inside the core lib (Services layer).
`IndexerOptions` mirrors the same pattern in its own host project
(`Minerva.MarkdownIndexer`) — no shared abstraction layer.
**Rationale**: Pragmatic Clean Architecture. The core lib's namespace acts as
the layer boundary. Mirroring beats premature generalization for one host
record.

### Raw DTOs

**Choice**: Raw DTOs are `internal`, mirror JSON shape, and are the only types
the `IConfiguration` binder ever sees. The public binder entry takes
`IConfiguration` and produces a validated record. Tests build in-memory
`IConfiguration` from JSON snippets and call the binder. **No
`InternalsVisibleTo` needed.**
**Rationale**: Keeps the binder-shaped ugliness fully encapsulated. Tests
exercise behavior — JSON in, validated record or exception out — not internal
shape.

### Defaults move from code to versioned appsettings.json

**Choice**: Code is default-free. The following defaults move into versioned
`appsettings.json` files in each host project:

- `EmbeddingProviderOptions.Concurrency` (was `1`)
- `EmbeddingProviderOptions.BatchSize` (was `1`)
- `LlmProviderOptions.Concurrency` (was `1`)
- `ChunkingOptions.TargetChunkSize` (was `1200`)
- `ChunkingOptions.ChunkOverlap` (was `200`)
- `ChunkingOptions.MaxSegmentChars` (was `8000`)
- `ChunkingOptions.ChunkerType` (was `Custom`)
- `IndexerOptions.ExcludeDirectories` (was `[]`)

Removed entirely: `LlmProviderOptions.BatchSize` (dead),
`ChunkingOptions.EnableSummarization` (replaced by Llm presence),
`ChunkingOptions.EnableContextualization` (same), `IndexerOptions.FilePattern`
(inlined as scanner constant).

**Rationale**: Versioned `appsettings.json` becomes the canonical baseline.
Per-environment overlays only override what differs.

### appsettings.json migration checklist

Every host's `appsettings.json` must end up with the keys its bound record
requires. The `Llm` section relocates from top-level to
`Chunking.Llm` for hosts that use augmentation.

- `src/Minerva.MarkdownIndexer/appsettings.json` and `appsettings.debug.json`:
  Embedding section complete; Chunking section complete with all keys; Llm
  section moved under Chunking; Indexer section retains `RootPath`,
  `CollectionName`, `ExcludeDirectories` (no `FilePattern`).
- `src/Minerva.Search.Cli/appsettings.json` and `appsettings.debug.json`:
  Embedding section complete; Chunking section padded with all required
  keys (temporary — see `minerva-search-options.md`); no Llm needed.
- `src/Minerva.ChunkComparator/appsettings.json`: Embedding + Chunking
  complete; Llm under Chunking if used.

### ApiKey handling

**Choice**: Both `EmbeddingProviderOptions.ApiKey` and
`LlmProviderOptions.ApiKey` are nullable. No record-level validation.
`ProviderFactory` keeps current resolver-or-fallback behavior. Missing key on
a cloud endpoint surfaces as a 401 at network preflight.
**Rationale**: Local LLMs (e.g. Ollama) genuinely do not need a key. Whether
a key is required for a given endpoint can only be known at runtime, so
preflight is the correct enforcement layer.

### Removal of MinervaBuilder.ValidateOptions

**Choice**: `MinervaBuilder.ValidateOptions` and the surrounding "Phase 1"
block in `src/Minerva/MinervaBuilder.cs` are deleted. Records arrive at the
builder pre-validated.

**Preserved**: The Phase-2 `try`/`catch (ConfigurationException)` blocks
around `ProviderFactory` construction stay. Env-var resolution failures
(`CredentialResolver` throwing on a missing `${ENV_VAR}`) are environmental,
not shape errors, and continue to surface as `MinervaStartupException`
preflight failures.

**Rationale**: Self-validating records remove the need for a duplicate
validator. Env-var resolution is a separate concern that already has a clean
fail path.

### Hosts wiring

Every host's `Program.cs` follows the same pattern:

```csharp
try
{
    var minervaOptions = MinervaOptionsBinder
        .Bind(builder.Configuration.GetSection("Minerva"));
    // …
    var engine = await MinervaBuilder.CreateAsync(minervaOptions, ...);
}
catch (OptionsValidationException ex) { /* exit code = same as MinervaStartupException */ }
catch (MinervaStartupException ex)    { /* preflight failure exit */ }
```

Affected files: `src/Minerva.MarkdownIndexer/Program.cs`,
`src/Minerva.Search.Cli/Program.cs`, `src/Minerva.ChunkComparator/Program.cs`.
`MarkdownIndexer` additionally calls `IndexerOptionsBinder.Bind(...)`.

## Approach Preferences

- Core types must not be shaped by external frameworks. Outer-layer adapters
  (raw DTOs, binders, factories) carry framework-flavored ugliness — they are
  "leaves in autumn on trees."
- Pragmatic Clean Architecture per the project's `.claude/CLAUDE.md`.
- No XML doc comments on options or DTOs.
- Big-bang migration in a single PR.

## Test Plan

Existing tests that construct option types directly will break and migrate to
factory-built records via shared helpers (likely under
`tests/Minerva.Tests/TestSupport/`):

- `tests/Minerva.Tests/Ingestion/DocumentChunkerTests.cs`
- `tests/Minerva.Tests/Ingestion/SplitMarkdownToBudgetTests.cs`
- `tests/Minerva.Tests/Watcher/MarkdownScannerTests.cs`
- `tests/Minerva.Tests/Providers/ProviderFactoryTests.cs`
- `tests/Minerva.IntegrationTests/EndToEnd/MinervaEngineE2ETests.cs`

New factory tests cover, at minimum:

- success path producing a fully-formed record;
- single missing/invalid field producing one `OptionsFailure` with the
  expected `Path`;
- multi-failure aggregation (sibling and child failures collected in one
  `OptionsValidationException`);
- nested composition: parent failure + child failure + grandchild failure all
  appear in the same exception;
- path accuracy (`Embedding.BaseUrl`, `Chunking.Llm.Model`, etc.);
- absence of `Chunking.Llm` produces a valid record with `Llm = null` (no
  augmentation).

The existing empty directories `tests/Minerva.Tests/Configuration/`,
`tests/Minerva.Tests/Models/`, `tests/Minerva.Tests/Fixtures/options/` will
be populated.

## Open Questions

None.

## Research Findings

- **LLM consumer audit**: `ILlmClient` is consumed only by
  `DocumentSummarizer` (`src/Minerva/Ingestion/DocumentSummarizer.cs`) and
  `ChunkContextualizer` (`src/Minerva/Ingestion/ChunkContextualizer.cs`).
  Confirms the design move: the LLM provider belongs nested under
  `ChunkingOptions`, not as a peer at the top level.

- **Concurrency / BatchSize usage audit**: `EmbeddingProviderOptions.BatchSize`
  is consumed by `EmbeddingService` via `MinervaBuilder.cs:78`.
  `EmbeddingProviderOptions.Concurrency` and `LlmProviderOptions.Concurrency`
  feed `RateLimiter` in `ProviderFactory.cs:31, 45`. **No code reads the
  LLM provider's `BatchSize`** — confirmed dead, hence the split.

- **ApiKey current handling**: `ProviderFactory` builds `CredentialResolver`
  if `ApiKey` is non-null, otherwise leaves the resolver null and the OpenAI
  client receives the literal `"no-key-required"` string at HTTP construction
  (`src/Minerva/Providers/ProviderFactory.cs:17-25, 53`).
  `ConfigurationException` is thrown by `CredentialResolver` for env-var
  resolution failures, by `CollectionManager`, and by `MinervaEngine`. None
  are about option shape.

- **Existing test coverage for option construction/validation**: directories
  `tests/Minerva.Tests/Configuration/`, `tests/Minerva.Tests/Models/`, and
  `tests/Minerva.Tests/Fixtures/options/` exist but are empty. No dedicated
  option-validation tests today. The five files listed in the Test Plan
  construct options to drive other behavior.

- **Working tree at planning time**: clean. Earlier system context referenced
  untracked files (`LlmContextBudgetOptions.cs`, `Configuration/ConfigReader.cs`)
  from a stale snapshot; those files do not exist and are not relevant to
  this refactor.
