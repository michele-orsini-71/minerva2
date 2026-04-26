
## Session 1 — 2026-04-25T15:27:45.178Z

<context>
## Context

**Goal**: Add a uniform `IReadinessCheck` pipeline to the Minerva core library so any client (today: `Minerva.MarkdownWatcher`; tomorrow: `minerva doctor` CLI / orchestrator GUI) can probe Postgres, pgvector, embedder, LLM, watched-folder, and embedding-dimension consistency *before* `host.RunAsync()`. Replaces today's hard crash inside `MinervaStartupService.StartAsync()` with a structured `ReadinessReport` carrying remediation hints, and exit code 2 from `Program.cs` when not ready.

**Design brief**: `.dev/preflight-and-manager/2026-04-21-preflight-and-manager.md` (12 numbered design decisions; non-negotiable inputs).

**Current phase**: Phase 1 — Readiness Core (Sub-PRD 01). Not yet started.

**Key completions this session**: PRD authored — `00-master-plan.md` and five sub-PRDs covering core, dimension provider, options relaxation, library checks, and watcher checks + Program.cs. Three parallel research agents verified the brief against live code and surfaced new findings. User-confirmed five open questions, resulting in concrete architectural decisions captured in the master plan.
</context>

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
- The `^[a-zA-Z0-9][a-zA-Z0-9-]*- Polly bypass is NOT a flag or config — both `OpenAICompatibleEmbeddingProvider` (line 44) and `OpenAICompatibleLlmProvider` (line 43) wrap every call in `_resiliencePipeline.ExecuteAsync(...)`. The bypass methods (`GetDimensionAsync`, `CheckAvailabilityAsync`) must call the OpenAI SDK `_client` directly, also skipping the `RateLimiter`. This is a new code path, not a configurable behaviour.
- Live DI bug to fix: `MarkdownSyncService` constructor injects `IEmbeddingGenerator<string, Embedding<float>>` which is never registered in DI today. Deletion in PRD 02 (replacing with `IEmbeddingDimensionProvider`) resolves the latent failure incidentally.
- PRD 03's options relaxation will NPE three sites in `ServiceCollectionExtensions.cs` (lines 28, 33-37, 62-64) plus `MarkdownSyncService.cs:95` unless guarded simultaneously. Build with nullable warnings as errors during PRD 03 to catch every site mechanically.
- Architecture tests (`tests/Minerva.ArchitectureTests/LayerDependencyTests.cs`) cover the `Minerva` assembly only — `Minerva.MarkdownWatcher` has no arch-test safety net for ring violations. Watcher checks must still respect the dependency direction, but no test will catch a regression there.
- The  regex now exists in three places: `CollectionManager.cs:84`, `SchemaInitializer.cs:106` (deliberate DDL-injection guard), and the upcoming `CollectionNameValidCheck`. Per design, this is NOT centralised — keep all three in sync manually.
- OpenAI SDK 2.10.0 — `ChatCompletionOptions.MaxOutputTokenCount` (not `MaxTokens`); `EmbeddingClient.GenerateEmbeddingsAsync(IEnumerable<string>, options, ct)`; both accept `CancellationToken`.
- `pg_available_extensions` may return empty rows on Azure / RDS for non-admin roles — the `PgVectorExtensionCheck` 'NOT_AVAILABLE' remediation must phrase it as 'either not installed OR your role cannot see it', not 'not installed' definitively.
- `Lazy<Task<int>>` cancellation discipline: factory uses `CancellationToken.None`; callers compose via `_lazy.Value.WaitAsync(ct)`. Factory must never throw synchronously — wrap as `async () => { … }` to ensure a Task is always returned. The wrong shape (capturing the first caller's CT in the factory) would let a transient first-caller timeout poison the cache permanently.
</notes>

---
