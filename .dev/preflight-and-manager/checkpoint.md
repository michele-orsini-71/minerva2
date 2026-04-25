---
branch: main
last_commit: f302a14 refactors interfaces to make architecture tests pass
uncommitted_changes: true
checkpointed: '2026-04-25T15:27:45.178Z'
---
Read the following PRD files in order:

1. 00-master-plan.md
2. 01-readiness-core.md
3. 02-embedding-dimension-provider.md
4. 03-options-relaxation.md
5. 04-builtin-library-checks.md
6. 05-watcher-checks-and-program.md

<context>
## Context

**Goal**: Add a uniform `IReadinessCheck` pipeline to the Minerva core library so any client (today: `Minerva.MarkdownWatcher`; tomorrow: `minerva doctor` CLI / orchestrator GUI) can probe Postgres, pgvector, embedder, LLM, watched-folder, and embedding-dimension consistency *before* `host.RunAsync()`. Replaces today's hard crash inside `MinervaStartupService.StartAsync()` with a structured `ReadinessReport` carrying remediation hints, and exit code 2 from `Program.cs` when not ready.

**Design brief**: `.dev/preflight-and-manager/2026-04-21-preflight-and-manager.md` (12 numbered design decisions; non-negotiable inputs).

**Current phase**: Phase 1 — Readiness Core (Sub-PRD 01). Not yet started.

**Key completions this session**: PRD authored — `00-master-plan.md` and five sub-PRDs covering core, dimension provider, options relaxation, library checks, and watcher checks + Program.cs. Three parallel research agents verified the brief against live code and surfaced new findings. User-confirmed five open questions, resulting in concrete architectural decisions captured in the master plan.
</context>

<current_state>
## Current Progress

**Overall**: 0/18 steps complete (0%).

- ⬜ Phase 1: Readiness Core (Sub-PRD 01) — 0/4
- ⬜ Phase 2: Embedding Dimension Provider (Sub-PRD 02) — 0/3
- ⬜ Phase 3: Options Relaxation (Sub-PRD 03) — 0/3
- ⬜ Phase 4: Built-in Library Checks (Sub-PRD 04) — 0/4
- ⬜ Phase 5: Watcher Checks and Program.cs (Sub-PRD 05) — 0/4

No implementation has started. The PRD is committed-ready but currently untracked.
</current_state>

<next_action>
## Next Steps

1. **Start Phase 1 (Sub-PRD 01)** — Readiness Core. First step: add the contracts and result types under `src/Minerva/Readiness/` (`ReadinessCategory`, `ReadinessCheckResult`, `ReadinessReport`, `IReadinessCheck`, `IReadinessChecker`). See `01-readiness-core.md` Step 1.
2. After Phase 1 lands, Phases 2 and 3 are independent and can be implemented in either order (or in parallel by separate workstreams).
3. Phases 4 and 5 are sequential and depend on 2 + 3.
4. The brief itself — `2026-04-21-preflight-and-manager.md` — should remain in the directory as a reference; do not modify it.
</next_action>

<key_files>
## Key Files

### PRD documents
- Master PRD: `.dev/preflight-and-manager/00-master-plan.md`
- Sub-PRD 01: `.dev/preflight-and-manager/01-readiness-core.md`
- Sub-PRD 02: `.dev/preflight-and-manager/02-embedding-dimension-provider.md`
- Sub-PRD 03: `.dev/preflight-and-manager/03-options-relaxation.md`
- Sub-PRD 04: `.dev/preflight-and-manager/04-builtin-library-checks.md`
- Sub-PRD 05: `.dev/preflight-and-manager/05-watcher-checks-and-program.md`
- Design brief (read-only reference): `.dev/preflight-and-manager/2026-04-21-preflight-and-manager.md`

### Source files that will be touched
- `src/Minerva/DI/MinervaStartupService.cs` — current crash path; will gain log-only safety-net warning (PRD 01)
- `src/Minerva/DI/ServiceCollectionExtensions.cs` — `AddMinerva()`; will become conditional on options (PRD 03), and register the readiness core + checks (PRD 04)
- `src/Minerva/Configuration/MinervaOptions.cs` — `Embedding` and `ConnectionString` become nullable (PRD 03)
- `src/Minerva/Embedding/OpenAICompatibleEmbeddingProvider.cs` — implements `IEmbeddingDimensionProvider` with `Lazy<Task<int>>` shape; bypasses Polly + RateLimiter (PRD 02)
- `src/Minerva/Llm/OpenAICompatibleLlmProvider.cs` — adds `CheckAvailabilityAsync()`; bypasses Polly + RateLimiter (PRD 04)
- `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs` — deletes `ProbeEmbeddingDimensionAsync` and dead `IEmbeddingGenerator` injection (PRD 02); null-guards `Embedding.Model` access (PRD 03)
- `src/Minerva.MarkdownWatcher/Program.cs` — current 4-line entry; rewrite as `async Task<int>` with pre-host preflight snippet (PRD 05)
- `tests/Minerva.IntegrationTests/Storage/StorageTestFixture.cs` — reuse pattern for new readiness integration tests (PRDs 04 + 05)
- `tests/Minerva.ArchitectureTests/LayerDependencyTests.cs` — add `Minerva.Readiness` (use-case ring) and `Minerva.Readiness.Checks` (adapter ring) partitions
</key_files>

<decisions>
- Integration tests reuse the existing `StorageTestFixture` env-var pattern (`MINERVA_TEST_CONNSTRING`); do NOT add Testcontainers.
- LLM availability probe uses `MaxOutputTokenCount = 5`; HTTP 400 from reasoning models that reject the params is treated as 'reachable, params rejected' → check still passes (reachability is the signal).
- DI registration becomes conditional when a feature's config block is null: `Embedding == null` skips embedding services; `ConnectionString == null` skips storage services; `Llm == null` already conditional today.
- `Redact` helper lives at `src/Minerva/Readiness/Redact.cs` (internal static; co-located with its only consumers).
- Feature is split into 5 sub-PRDs (compressed from initial 7) with tests distributed within each sub-PRD rather than aggregated.
</decisions>

<notes>
- Polly bypass is NOT a flag or config — both `OpenAICompatibleEmbeddingProvider` (line 44) and `OpenAICompatibleLlmProvider` (line 43) wrap every call in `_resiliencePipeline.ExecuteAsync(...)`. The bypass methods (`GetDimensionAsync`, `CheckAvailabilityAsync`) must call the OpenAI SDK `_client` directly, also skipping the `RateLimiter`. This is a new code path, not a configurable behaviour.
- Live DI bug to fix: `MarkdownSyncService` constructor injects `IEmbeddingGenerator<string, Embedding<float>>` which is never registered in DI today. Deletion in PRD 02 (replacing with `IEmbeddingDimensionProvider`) resolves the latent failure incidentally.
- PRD 03's options relaxation will NPE three sites in `ServiceCollectionExtensions.cs` (lines 28, 33-37, 62-64) plus `MarkdownSyncService.cs:95` unless guarded simultaneously. Build with nullable warnings as errors during PRD 03 to catch every site mechanically.
- Architecture tests (`tests/Minerva.ArchitectureTests/LayerDependencyTests.cs`) cover the `Minerva` assembly only — `Minerva.MarkdownWatcher` has no arch-test safety net for ring violations. Watcher checks must still respect the dependency direction, but no test will catch a regression there.
- The `^[a-zA-Z0-9][a-zA-Z0-9-]*$` regex now exists in three places: `CollectionManager.cs:84`, `SchemaInitializer.cs:106` (deliberate DDL-injection guard), and the upcoming `CollectionNameValidCheck`. Per design, this is NOT centralised — keep all three in sync manually.
- OpenAI SDK 2.10.0 — `ChatCompletionOptions.MaxOutputTokenCount` (not `MaxTokens`); `EmbeddingClient.GenerateEmbeddingsAsync(IEnumerable<string>, options, ct)`; both accept `CancellationToken`.
- `pg_available_extensions` may return empty rows on Azure / RDS for non-admin roles — the `PgVectorExtensionCheck` 'NOT_AVAILABLE' remediation must phrase it as 'either not installed OR your role cannot see it', not 'not installed' definitively.
- `Lazy<Task<int>>` cancellation discipline: factory uses `CancellationToken.None`; callers compose via `_lazy.Value.WaitAsync(ct)`. Factory must never throw synchronously — wrap as `async () => { … }` to ensure a Task is always returned. The wrong shape (capturing the first caller's CT in the factory) would let a transient first-caller timeout poison the cache permanently.
</notes>

---

Continue work on the Minerva preflight readiness-checks feature. Read `.dev/preflight-and-manager/checkpoint.md` for full context, then start Phase 1 (Sub-PRD 01 — Readiness Core) by following `.dev/preflight-and-manager/01-readiness-core.md`. The PRD is authored but no implementation has started. The design brief at `.dev/preflight-and-manager/2026-04-21-preflight-and-manager.md` is the non-negotiable input — do not modify it.
