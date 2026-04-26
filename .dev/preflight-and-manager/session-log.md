
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

## Session 2 — 2026-04-26T07:01:30.750Z

<context>
## Context

**Goal**: Add a uniform `IReadinessCheck` pipeline to the Minerva core library so any client (today: `Minerva.MarkdownWatcher`; tomorrow: a `minerva doctor` CLI / orchestrator GUI) can probe Postgres, pgvector, embedder, LLM, watched-folder, and embedding-dimension consistency *before* `host.RunAsync()`.

**Phase 1 (Readiness Core, Sub-PRD 01) — DONE**: contracts, sequential `ReadinessChecker` with per-check timeout, `Redact` helper, `ReadinessProbeMarker`, `ReadinessReportFormatter`, DI extension, and a safety-net warning in `MinervaStartupService` when the marker exists but `Probed == false`. Architecture tests updated for the new `Minerva.Readiness` use-case-ring partition.

**Verification (all green this session)**: `dotnet build Minerva.sln` clean; `dotnet test Minerva.Tests --filter Category=Readiness` 17 passed; `dotnet test Minerva.ArchitectureTests` 12 passed (including the new `Readiness_DoesNotDependOnAdaptersOrFramework` rule and `NamespaceCoverageTests` classification).
</context>

<decisions>
- Tests live in the existing `Minerva.Tests` project (not `Minerva.UnitTests` as the PRD wrote) — matches `InternalsVisibleTo` and existing convention. Filtered via `[Trait("Category", "Readiness")]`.
- `Redact` uses plain compiled `Regex` (`RegexOptions.Compiled`), not `[GeneratedRegex]` partial methods. Reason: the IDE language server did not run the source generator and reported false 'partial method must have an implementation part' errors. Behavior is identical; the analyzer hint suggesting `[GeneratedRegex]` is intentionally ignored.
- Hand-rolled `RecordingLogger<T>` (in `tests/Minerva.Tests/Readiness/`) is the project's first ILogger test double — used for `ReadinessReportFormatter` and `MinervaStartupService` warning assertions because no prior NSubstitute-on-`Log<TState>` pattern exists in the repo and intercepting the generic state is awkward.
- `MinervaStartupService` constructor params became `IReadinessProbeMarker? marker = null, ILogger<MinervaStartupService>? logger = null` (defaults to `NullLogger`) so any future direct-construction call site keeps working. Today there are no such sites — verified via grep.
- `MinervaStartupServiceWarningTests` builds a `SchemaInitializer` over `Host=localhost;Port=1` and never awaits the returned `Task` from `StartAsync`. The warning is logged synchronously in `StartAsync` before `_schemaInitializer.InitializeAsync(ct)` is invoked, so the assertion is observable without a live Postgres.
- Architecture tests required two updates beyond what the PRD specified: (a) a new `Readiness_DoesNotDependOnAdaptersOrFramework` test in `LayerDependencyTests.cs`; (b) `Minerva.Readiness` registered in `NamespaceCoverageTests.ClassifiedNamespaces` as `USE_CASES` — discovered when the coverage test failed on the first arch-test run. Existing inner-ring `NotHaveDependencyOnAny` lists were also extended to forbid Models/Exceptions/Utilities from depending on `Minerva.Readiness`.
</decisions>

<notes>
- PRD-vs-disk drift: every sub-PRD references `tests/Minerva.UnitTests/...`, but the project is `tests/Minerva.Tests/`. Translate paths in every future phase rather than renaming the test project.
- IDE C# language server can show false errors that `dotnet build` doesn't reproduce (source-generator scenarios). Treat the CLI build as the source of truth.
- Phase 2 must use the Polly+RateLimiter bypass discipline noted in the prior checkpoint: `OpenAICompatibleEmbeddingProvider` line 44 wraps every call in `_resiliencePipeline.ExecuteAsync(...)`. The dimension lookup must call the OpenAI SDK `_client` directly, also skipping the `RateLimiter`.
- Phase 2 will incidentally fix the live DI bug where `MarkdownSyncService` injects an unregistered `IEmbeddingGenerator<string, Embedding<float>>`.
</notes>

---

## Session 3 — 2026-04-26T07:44:23.896Z

<context>
## Context

**Goal**: Add a uniform `IReadinessCheck` pipeline to the Minerva core library so any client (today: `Minerva.MarkdownWatcher`; tomorrow: `minerva doctor` CLI / orchestrator GUI) can probe Postgres, pgvector, embedder, LLM, watched-folder, and embedding-dimension consistency *before* `host.RunAsync()`.

**Phase 1 (Readiness Core, Sub-PRD 01) — DONE** prior session.

**Phase 2 (Embedding Dimension Provider, Sub-PRD 02) — DONE this session**: introduced `IEmbeddingDimensionProvider` port; implemented on `OpenAICompatibleEmbeddingProvider` with a `Lazy<Task<int>>` field whose factory uses `CT.None` (caller cancellation composes via `.WaitAsync(ct)`); routed the dimension probe through an internal `IEmbeddingProbeFacade` seam that calls the OpenAI SDK directly — bypassing both `_resiliencePipeline` (Polly) and `_rateLimiter`; SDK exceptions translate to `ProviderUnavailableException`; `OperationCanceledException` rethrows as-is. DI now registers the concrete provider as singleton with forwarding registrations for both `IEmbeddingClient` and `IEmbeddingDimensionProvider`. `MarkdownSyncService` switched to inject `IEmbeddingDimensionProvider`; deleted the local `ProbeEmbeddingDimensionAsync` helper and the never-registered `IEmbeddingGenerator<string, Embedding<float>>` parameter (latent DI bug fixed incidentally).

**Verification (all green)**: `dotnet build Minerva.sln` clean (0 warnings, 0 errors); `dotnet test tests/Minerva.Tests` 141 passed (6 new dimension-provider tests covering cancellation-poisoning, failure-caching, success-caching, Polly-bypass, ClientResultException translation, and DI lifetime alignment); `dotnet test tests/Minerva.ArchitectureTests` 12 passed.
</context>

<decisions>
- `IEmbeddingDimensionProvider` lives in `src/Minerva/Ingestion/` (the existing ports namespace alongside `IEmbeddingClient`), NOT in `src/Minerva/Embedding/` as the PRD suggested. Reason: matches port-vs-adapter convention, avoids registering a new namespace in `NamespaceCoverageTests.ClassifiedNamespaces`.
- Test seam shape: an `internal IEmbeddingProbeFacade` interface with a single method `EmbedAndCountDimensionsAsync(string, CancellationToken)`. Default impl `SdkEmbeddingProbeFacade` wraps `_client.GenerateEmbeddingsAsync` directly. A private internal ctor on `OpenAICompatibleEmbeddingProvider` accepts a facade override; tests use it. The OpenAI SDK `EmbeddingClient` is sealed and has no virtual surface — this is the minimum abstraction needed.
- DI pattern is concrete-singleton + forwarding: `TryAddSingleton<OpenAICompatibleEmbeddingProvider>(sp => factory.CreateEmbeddingProvider(...))` then `TryAddSingleton<IEmbeddingClient>(sp => sp.GetRequiredService<OpenAICompatibleEmbeddingProvider>())` and the same for `IEmbeddingDimensionProvider`. Verified single-instance via `Assert.Same` test.
- `ProbeDimensionCoreAsync` translates `ClientResultException` and generic `Exception` to `ProviderUnavailableException`. `OperationCanceledException` rethrows untouched so callers distinguish cancellation from probe failure.
- Test file path: `tests/Minerva.Tests/Providers/EmbeddingDimensionProviderTests.cs` — PRD said `tests/Minerva.UnitTests/Embedding/...`. Honoring prior session decision: translate paths, do not rename the test project.
- `OpenAICompatibleEmbeddingProvider` now implements three interfaces: `IEmbeddingGenerator<string, Embedding<float>>`, `IEmbeddingClient`, `IEmbeddingDimensionProvider`. The `IEmbeddingGenerator<,>` implementation is preserved unchanged for backwards compatibility — not used by `MarkdownSyncService` anymore but remains part of the public surface.
</decisions>

<notes>
- Polly+RateLimiter bypass is real for `ProbeDimensionCoreAsync` — `_resiliencePipeline.ExecuteAsync` is NOT involved on this path; the facade calls `_client.GenerateEmbeddingsAsync` directly. The `Polly bypass` test asserts `CallCount == 1` after a transient `HttpRequestException`, which Polly's retry strategy WOULD have retried 3 more times.
- Failure caching is by design (Decision 12 in the brief). The `Lazy<Task<int>>` caches a faulted Task — subsequent `GetDimensionAsync` calls rethrow without re-invoking the facade. The `FailureIsCachedAcrossCalls` test asserts call count stays at 1 across two sequential failures.
- Cancellation-poisoning test gates the facade with a `TaskCompletionSource<int>` (RunContinuationsAsynchronously) — caller A awaits with a 50ms cancelling CT and throws; caller B awaits with `CancellationToken.None`; when the gate is set, B observes the dimension. `CallCount == 1` confirms the underlying probe ran exactly once across both callers.
- DI registration order in `ServiceCollectionExtensions.cs` matters: concrete singleton MUST be registered before the two forwarding registrations, otherwise the forwarders trigger the (missing) factory and either fail or produce a second instance.
- PRD-vs-disk drift continues: all remaining sub-PRDs reference `tests/Minerva.UnitTests/...`. Translate to `tests/Minerva.Tests/...` for every future phase.
- Phase 3 (Options Relaxation) will need nullable-warnings-as-errors during the build to catch every NPE site mechanically — `ServiceCollectionExtensions.cs:28,33-37,62-64` were called out in the prior session.
</notes>

---
