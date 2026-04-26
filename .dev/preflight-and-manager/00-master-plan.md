# Minerva Preflight Readiness Checks - Master Plan

**Status**: In Progress
**Created**: 2026-04-25
**Last Updated**: 2026-04-26

---

## Executive Summary

Add a uniform `IReadinessCheck` pipeline to the `Minerva` core library so any client (today: `Minerva.MarkdownWatcher`; tomorrow: a `minerva doctor` CLI, an orchestrator GUI) can run a structured pre-host probe of every prerequisite — Postgres reachability, `pgvector` availability, embedder/LLM reachability, watched-folder existence, embedding-dimension consistency — *before* `host.RunAsync()`. Replaces today's hard crash inside `MinervaStartupService.StartAsync()` (`NpgsqlException` from `SchemaInitializer.InitializeAsync()` aborting the process) with a `ReadinessReport` carrying remediation hints, and a standardized exit code `2` from `Program.cs` when not ready.

**Reference**: `.dev/preflight-and-manager/2026-04-21-preflight-and-manager.md` (the design brief — 12 numbered decisions and approach preferences are non-negotiable inputs).

---

## Research Findings

### Codebase Patterns

- **Crash path**: `MinervaStartupService.StartAsync()` → `SchemaInitializer.InitializeAsync()` → `_dataSource.OpenConnectionAsync()`. As an `IHostedService`, unhandled exceptions abort the process. Preflight attaches **before** this, in `Program.cs`.
- **Existing integration-test infra**: `tests/Minerva.IntegrationTests/Storage/StorageTestFixture.cs` provides an `IAsyncLifetime` fixture against a local Postgres via env-var `MINERVA_TEST_CONNSTRING` (with localhost default). Reuse this — do NOT add Testcontainers.
- **Provider Polly pipeline lives INSIDE the providers**: `OpenAICompatibleEmbeddingProvider.cs:44` and `OpenAICompatibleLlmProvider.cs:43` wrap every call in `_resiliencePipeline.ExecuteAsync(...)`. There is no external bypass — preflight methods on these classes must call the OpenAI SDK `_client` directly to skip both Polly and the `RateLimiter`.
- **Exception hierarchy**: `MinervaException` is the root; `EmbeddingException : IngestionException : MinervaException`; `ProviderUnavailableException : MinervaException`. Checks catch broadly, translate to failed results.
- **`MinervaOptions` shape today**: `Llm` is nullable; `Embedding` and `ConnectionString` are `required`. This feature relaxes the latter two.
- **Architecture tests** (`tests/Minerva.ArchitectureTests/LayerDependencyTests.cs`) cover the `Minerva` assembly only, with ring-based dependency rules. New `Minerva.Readiness` namespace must be added to the use-case ring; check classes that touch `Npgsql` belong in the adapter ring.

### Dependencies

- **Npgsql / Postgres**: connectivity, `pg_extension`, `pg_available_extensions` queries.
- **OpenAI SDK 2.10.0**: `EmbeddingClient.GenerateEmbeddingsAsync`, `ChatClient.CompleteChatAsync` (with `ChatCompletionOptions.MaxOutputTokenCount`).
- **`StorageTestFixture`**: shared integration-test fixture for live Postgres tests.

### Technical Decisions

| Decision | Rationale | Alternatives Considered |
|----------|-----------|------------------------|
| Unified `IReadinessCheck` pipeline | One surface, uniform UX, uniform report — pays off the moment a second client appears | Parallel ad-hoc checks per client (rejected: boilerplate) |
| Probe + build dual API | A probe that does not require building the engine enables a future `minerva doctor` CLI | Single `BuildAndCheck` (rejected: couples lifecycle) |
| Preflight runs in `Program.cs` before `host.RunAsync()` | Explicit, library stays unopinionated about hosting | Move into `MinervaStartupService` (rejected: refactors hosting; hard to share with non-host clients) |
| Direct call to provider for availability (not model listing) | One step validates endpoint + auth + model availability; sidesteps `/v1/models` 403, OpenAI-vs-Ollama discriminators, model-name normalisation | List-models endpoint (rejected) |
| No severity field; conditional execution via short-circuit | Avoids the "should this warning block startup?" policy question; works around DI-timing trap (`IOptions<>` not bound at registration) | Severity enum (rejected) |
| `Code` field on `ReadinessCheckResult` | Stable machine-readable identity (`MINERVA.POSTGRES.CONNECTION_FAILED` etc.) | Numeric codes; URN-style (rejected: less readable) |
| Sequential execution, per-check timeout, no retry | Deterministic logs; preflight is fail-fast, retries make it worse | Parallel + DAG (rejected: complexity) |
| `IEmbeddingDimensionProvider` with `Lazy<Task<int>>` (factory uses `CT.None`) | Eliminates duplicate embedding call between preflight and runtime, without sharing mutable state | Threading dimension as side-effect; reprobing at runtime |
| LLM probe `MaxOutputTokenCount = 5`; HTTP 400 → passes | Reasoning models may reject 1-token caps; reachability is the signal we want | `MaxOutputTokenCount = 1` (rejected: false negative on reasoning models) |
| Conditional DI registration when feature config is null | Clean container surface; no sentinels needed | No-op singletons (rejected: introduces null-object types) |
| `Redact` helper at `src/Minerva/Readiness/Redact.cs` | Co-located with its only consumers | `src/Minerva/Diagnostics/` (rejected: creates a one-file namespace) |
| Five sub-PRDs (split per seam, tests distributed in each) | Each lands as a small-to-medium PR; tests stay close to changes | One big PRD; seven sub-PRDs (rejected: too granular) |

### Constraints

- **Reuse**: `StorageTestFixture` for integration tests; existing `MinervaException` hierarchy; existing `^[a-zA-Z0-9][a-zA-Z0-9-]*$` regex (which already exists in two places — `CollectionManager.cs:84` and `SchemaInitializer.cs:106` as a deliberate DDL-injection guard; the new `CollectionNameValidCheck` is a third copy on purpose).
- **Patterns to follow**: `IOptions<T>` for configuration; DI via `Microsoft.Extensions.DependencyInjection`; logger categories on the implementing class; record types for value-shaped results.
- **Avoid**: Testcontainers (existing pattern uses env-var fixture); centralising the collection-name regex (the SchemaInitializer copy is deliberate); making preflight automatic inside `MinervaStartupService` (explicit-in-`Program.cs` is by design).

---

## Architecture Decision

**Approach**: Library-level readiness pipeline + explicit pre-host invocation in the client.

The library exports `IReadinessCheck`, `IReadinessChecker`, and a DI extension `AddMinervaReadinessCheck<T>()`. `AddMinerva()` registers the five built-in library checks; `AddMinervaWatcher()` registers three watcher checks. The watcher's `Program.cs` builds the host, opens a DI scope, runs the checker, formats the report, and either exits with code 2 (not ready) or proceeds with `host.RunAsync()`. `MinervaStartupService` checks an `IReadinessProbeMarker` flag and emits a log warning if the probe was skipped — observability only, no behaviour change.

Embedding-dimension discovery is unified behind `IEmbeddingDimensionProvider` with a memoized `Lazy<Task<int>>` so preflight and runtime share one network call.

**Data flow**:

```
Program.cs:
  builder.Build()
       │
       ▼
  scope.Resolve(IReadinessChecker)
       │
       ▼
  checker.CheckReadinessAsync()
       │
       ├── IReadinessCheck (sequential, per-check timeout)
       │     ├── ConnectionStringParseCheck    (Configuration)
       │     ├── PostgresConnectivityCheck     (Storage)
       │     ├── PgVectorExtensionCheck        (Storage)
       │     ├── EmbeddingCallCheck            (Embedding)  ──┐
       │     ├── LlmCallCheck                  (Llm)          │
       │     ├── RootPathExistsCheck           (Client)       │ shared cache
       │     ├── CollectionNameValidCheck      (Client)       │ via
       │     └── CollectionDimensionMatchCheck (Client)  ─────┤ IEmbeddingDimensionProvider
       │                                                      │ (Lazy<Task<int>>)
       ▼                                                      │
  ReadinessReport ──► Formatter ──► ILogger                   │
       │                                                      │
       ├── IsReady = true  ──► host.RunAsync() ──► return 0   │
       │       │                                              │
       │       └── MinervaStartupService checks marker        │
       │           (set by checker) — no warning              │
       │                                                      │
       └── IsReady = false ──► return 2                       │
                                                              │
  unhandled exception in Main                ──► return 1     │
                                                              ▼
                                                   OpenAICompatibleEmbeddingProvider
```

---

## Sub-PRD Overview

| Sub-PRD | Title | Dependency | Status | Document |
|---------|-------|------------|--------|----------|
| **1** | Readiness Core | None | Done | [01-readiness-core.md](./01-readiness-core.md) |
| **2** | Embedding Dimension Provider | 1 | Not Started | [02-embedding-dimension-provider.md](./02-embedding-dimension-provider.md) |
| **3** | Options Relaxation | 1 | Not Started | [03-options-relaxation.md](./03-options-relaxation.md) |
| **4** | Built-in Library Checks | 2, 3 | Not Started | [04-builtin-library-checks.md](./04-builtin-library-checks.md) |
| **5** | Watcher Checks and Program.cs | 4 | Not Started | [05-watcher-checks-and-program.md](./05-watcher-checks-and-program.md) |

Sub-PRDs 2 and 3 are independent of each other and may be implemented in parallel after 1 lands.

---

## Implementation Order

### Phase 1: Readiness Core (Sub-PRD 01)
**Goal**: Establish contracts and machinery; no checks yet, no library auto-registration.

1. ✅ Add contracts and result types
2. ✅ Add `Redact` helper, `IReadinessProbeMarker`, `ReadinessChecker` impl
3. ✅ Add `ReadinessReportFormatter`, DI extension, safety-net warning in `MinervaStartupService`
4. ✅ Update architecture tests for the `Minerva.Readiness` namespace; add unit tests

**Verification**:
- [x] `dotnet build src/Minerva/Minerva.csproj` — clean
- [x] `dotnet test tests/Minerva.Tests --filter Category=Readiness` — 17 passed
- [x] `dotnet test tests/Minerva.ArchitectureTests` — 12 passed

⏸️ **GATE**: Phase complete. Continue or `/dev-checkpoint`.

### Phase 2: Embedding Dimension Provider (Sub-PRD 02)
**Goal**: Lift dimension into a memoized provider accessor; remove duplicate runtime probe.

1. ⬜ Add `IEmbeddingDimensionProvider`; implement on `OpenAICompatibleEmbeddingProvider` with `Lazy<Task<int>>` (factory uses `CT.None`); bypass Polly + RateLimiter
2. ⬜ Switch `MarkdownSyncService` to inject the provider; delete `ProbeEmbeddingDimensionAsync`; remove dead `IEmbeddingGenerator` injection
3. ⬜ Add unit tests including cancellation-poisoning + failure-caching

**Verification**:
- [ ] `dotnet build`
- [ ] No remaining references to `ProbeEmbeddingDimensionAsync`
- [ ] `dotnet test tests/Minerva.UnitTests`

⏸️ **GATE**: Phase complete. Continue or `/dev-checkpoint`.

### Phase 3: Options Relaxation (Sub-PRD 03)
**Goal**: Make `MinervaOptions.Embedding` and `ConnectionString` nullable; conditional DI registration.

1. ⬜ Drop `required`; mark `Embedding` and `ConnectionString` nullable
2. ⬜ Conditional registration in `AddMinerva()` (storage iff `ConnectionString != null`; embedding iff `Embedding != null`); guard remaining call sites
3. ⬜ Tests for no-config, partial-config, and full-config DI builds

**Verification**:
- [ ] `services.AddMinerva(_ => { }).BuildServiceProvider()` does not throw
- [ ] `dotnet test tests/Minerva.UnitTests`

⏸️ **GATE**: Phase complete. Continue or `/dev-checkpoint`.

### Phase 4: Built-in Library Checks (Sub-PRD 04)
**Goal**: Five library checks + LLM probe; auto-registered by `AddMinerva()`.

1. ⬜ Add `OpenAICompatibleLlmProvider.CheckAvailabilityAsync()` (one-shot, bypass Polly + RateLimiter, `MaxOutputTokenCount = 5`, HTTP 400 → passes)
2. ⬜ Implement `ConnectionStringParseCheck`, `PostgresConnectivityCheck`, `PgVectorExtensionCheck`, `EmbeddingCallCheck`, `LlmCallCheck` with per-check timeouts and short-circuit branches
3. ⬜ Register from `AddMinerva()`
4. ⬜ Unit tests + integration tests against `StorageTestFixture`

**Verification**:
- [ ] `dotnet test tests/Minerva.UnitTests`
- [ ] `MINERVA_TEST_CONNSTRING=… dotnet test tests/Minerva.IntegrationTests`
- [ ] `dotnet test tests/Minerva.ArchitectureTests`

⏸️ **GATE**: Phase complete. Continue or `/dev-checkpoint`.

### Phase 5: Watcher Checks and Program.cs (Sub-PRD 05)
**Goal**: Three watcher checks + explicit pre-host snippet; end-to-end exit-2 path works.

1. ⬜ Implement `RootPathExistsCheck`, `CollectionNameValidCheck`, `CollectionDimensionMatchCheck` (four Decision-10 branches)
2. ⬜ Register from `AddMinervaWatcher()`
3. ⬜ Rewrite `Program.cs` as `async Task<int>` with the explicit pre-host snippet (return 0/1/2)
4. ⬜ Unit + integration tests; manual end-to-end exit-code verification

**Verification**:
- [ ] Healthy environment: watcher starts and runs as today
- [ ] Postgres unreachable: watcher exits with `$?` = 2 and logs the report
- [ ] `dotnet test` across all test projects

⏸️ **GATE**: Feature complete. `/dev-checkpoint` or merge.

---

## File Changes Summary

### New Files

| File | Purpose |
|------|---------|
| `src/Minerva/Readiness/ReadinessCategory.cs` | Enum |
| `src/Minerva/Readiness/ReadinessCheckResult.cs` | Result record |
| `src/Minerva/Readiness/ReadinessReport.cs` | Report record |
| `src/Minerva/Readiness/IReadinessCheck.cs` | Check interface |
| `src/Minerva/Readiness/IReadinessChecker.cs` | Checker interface |
| `src/Minerva/Readiness/ReadinessChecker.cs` | Sequential implementation |
| `src/Minerva/Readiness/IReadinessProbeMarker.cs` | Marker interface |
| `src/Minerva/Readiness/ReadinessProbeMarker.cs` | Marker impl |
| `src/Minerva/Readiness/Redact.cs` | Internal redaction helper |
| `src/Minerva/Readiness/ReadinessReportFormatter.cs` | Public log helper |
| `src/Minerva/Readiness/ReadinessServiceCollectionExtensions.cs` | DI extensions |
| `src/Minerva/Readiness/Checks/ConnectionStringParseCheck.cs` | Configuration check |
| `src/Minerva/Readiness/Checks/PostgresConnectivityCheck.cs` | Storage check |
| `src/Minerva/Readiness/Checks/PgVectorExtensionCheck.cs` | Storage check |
| `src/Minerva/Readiness/Checks/EmbeddingCallCheck.cs` | Embedding check |
| `src/Minerva/Readiness/Checks/LlmCallCheck.cs` | LLM check |
| `src/Minerva/Embedding/IEmbeddingDimensionProvider.cs` | Memoized dimension accessor |
| `src/Minerva.MarkdownWatcher/Readiness/RootPathExistsCheck.cs` | Watcher check |
| `src/Minerva.MarkdownWatcher/Readiness/CollectionNameValidCheck.cs` | Watcher check |
| `src/Minerva.MarkdownWatcher/Readiness/CollectionDimensionMatchCheck.cs` | Watcher check |
| `tests/Minerva.UnitTests/Readiness/**` | Unit tests for core + checks |
| `tests/Minerva.UnitTests/Embedding/EmbeddingDimensionProviderTests.cs` | Concurrency/cancellation/caching tests |
| `tests/Minerva.UnitTests/DI/AddMinervaConditionalRegistrationTests.cs` | Conditional DI tests |
| `tests/Minerva.UnitTests/DI/MinervaStartupServiceWarningTests.cs` | Safety-net warning |
| `tests/Minerva.IntegrationTests/Readiness/PostgresConnectivityCheckIntegrationTests.cs` | Live DB |
| `tests/Minerva.IntegrationTests/Readiness/PgVectorExtensionCheckIntegrationTests.cs` | Live DB |
| `tests/Minerva.IntegrationTests/Readiness/CollectionDimensionMatchCheckIntegrationTests.cs` | Live DB |
| `tests/Minerva.MarkdownWatcher.UnitTests/Readiness/**` | Watcher check unit tests |

### Modified Files

| File | Changes |
|------|---------|
| `src/Minerva/Configuration/MinervaOptions.cs` | Drop `required` from `Embedding` and `ConnectionString`; nullable |
| `src/Minerva/DI/ServiceCollectionExtensions.cs` | Conditional registration; register the five library checks; register `IReadinessChecker` core |
| `src/Minerva/DI/MinervaStartupService.cs` | Inject optional `IReadinessProbeMarker`; log warning if not probed |
| `src/Minerva/Embedding/OpenAICompatibleEmbeddingProvider.cs` | Implement `IEmbeddingDimensionProvider`; `Lazy<Task<int>>` shape; Polly + RateLimiter bypass |
| `src/Minerva/Llm/OpenAICompatibleLlmProvider.cs` | Add `CheckAvailabilityAsync()` bypassing Polly + RateLimiter |
| `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs` | Inject `IEmbeddingDimensionProvider`; delete `ProbeEmbeddingDimensionAsync`; remove dead `IEmbeddingGenerator` |
| `src/Minerva.MarkdownWatcher/DI/ServiceCollectionExtensions.cs` | Register the three watcher checks |
| `src/Minerva.MarkdownWatcher/Program.cs` | `async Task<int>` with explicit pre-host preflight snippet (exit 0/1/2) |
| `tests/Minerva.ArchitectureTests/LayerDependencyTests.cs` | Recognise `Minerva.Readiness` and `Minerva.Readiness.Checks` namespaces |

---

## Cross-Cutting Acceptance Criteria

- **Watcher exit-2 path**: with Postgres unreachable, the watcher exits with code 2; logs contain a `LogError` per failed check with `Code`, `Message`, and `Remediation`; no secrets (passwords, bearer tokens) appear in any logged `Message`.
- **Healthy-path startup**: with everything OK, `CheckReadinessAsync` returns `IsReady = true`, the host starts, schema initialises, watcher runs as today.
- **Architecture tests still pass**: `dotnet test tests/Minerva.ArchitectureTests` green; `Minerva.Readiness` namespace partition added.
- **Existing integration tests still pass**: `dotnet test tests/Minerva.IntegrationTests` green against `MINERVA_TEST_CONNSTRING`.
- **Safety-net warning fires**: build a host with `AddMinerva()` and `RunAsync()` immediately (no probe) — warning logged once at `Warning` level.
- **No-config `AddMinerva()` builds**: `services.AddMinerva(_ => { }).BuildServiceProvider()` does not throw.

---

## Reference Files

- `.dev/preflight-and-manager/2026-04-21-preflight-and-manager.md` — design brief (12 numbered decisions; non-negotiable inputs)
- `src/Minerva/DI/MinervaStartupService.cs` — current crash-on-misconfig path
- `src/Minerva/DI/ServiceCollectionExtensions.cs` — `AddMinerva()` definition
- `src/Minerva/Storage/SchemaInitializer.cs` — first I/O on the crash path
- `src/Minerva/Embedding/OpenAICompatibleEmbeddingProvider.cs` — embedding provider
- `src/Minerva/Llm/OpenAICompatibleLlmProvider.cs` — LLM provider
- `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs` — current dimension-probe site (to be deleted)
- `src/Minerva.MarkdownWatcher/Program.cs` — current 4-line entry point (to be rewritten)
- `tests/Minerva.IntegrationTests/Storage/StorageTestFixture.cs` — reuse pattern for integration tests
- `tests/Minerva.ArchitectureTests/LayerDependencyTests.cs` — ring-rule constraints to update
