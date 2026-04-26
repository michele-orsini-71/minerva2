---
branch: main
last_commit: f03f469 checkpoint saved
uncommitted_changes: true
checkpointed: '2026-04-26T07:44:23.896Z'
---
Read the following PRD files in order:

1. 00-master-plan.md
2. 01-readiness-core.md
3. 02-embedding-dimension-provider.md
4. 03-options-relaxation.md
5. 04-library-checks.md
6. 05-watcher-checks.md
7. 2026-04-21-preflight-and-manager.md

<context>
## Context

**Goal**: Add a uniform `IReadinessCheck` pipeline to the Minerva core library so any client (today: `Minerva.MarkdownWatcher`; tomorrow: `minerva doctor` CLI / orchestrator GUI) can probe Postgres, pgvector, embedder, LLM, watched-folder, and embedding-dimension consistency *before* `host.RunAsync()`.

**Phase 1 (Readiness Core, Sub-PRD 01) — DONE** prior session.

**Phase 2 (Embedding Dimension Provider, Sub-PRD 02) — DONE this session**: introduced `IEmbeddingDimensionProvider` port; implemented on `OpenAICompatibleEmbeddingProvider` with a `Lazy<Task<int>>` field whose factory uses `CT.None` (caller cancellation composes via `.WaitAsync(ct)`); routed the dimension probe through an internal `IEmbeddingProbeFacade` seam that calls the OpenAI SDK directly — bypassing both `_resiliencePipeline` (Polly) and `_rateLimiter`; SDK exceptions translate to `ProviderUnavailableException`; `OperationCanceledException` rethrows as-is. DI now registers the concrete provider as singleton with forwarding registrations for both `IEmbeddingClient` and `IEmbeddingDimensionProvider`. `MarkdownSyncService` switched to inject `IEmbeddingDimensionProvider`; deleted the local `ProbeEmbeddingDimensionAsync` helper and the never-registered `IEmbeddingGenerator<string, Embedding<float>>` parameter (latent DI bug fixed incidentally).

**Verification (all green)**: `dotnet build Minerva.sln` clean (0 warnings, 0 errors); `dotnet test tests/Minerva.Tests` 141 passed (6 new dimension-provider tests covering cancellation-poisoning, failure-caching, success-caching, Polly-bypass, ClientResultException translation, and DI lifetime alignment); `dotnet test tests/Minerva.ArchitectureTests` 12 passed.
</context>

<current_state>
## Current Progress

- ✅ Phase 1: Readiness Core (Sub-PRD 01) — 4/4
- ✅ Phase 2: Embedding Dimension Provider (Sub-PRD 02) — 3/3
- ⬜ Phase 3: Options Relaxation (Sub-PRD 03) — 0/3 — NEXT
- ⬜ Phase 4: Built-in Library Checks (Sub-PRD 04) — 0/4
- ⬜ Phase 5: Watcher Checks and Program.cs (Sub-PRD 05) — 0/4

**Overall**: 7/18 (39%). At gate between Phase 2 and Phase 3.
</current_state>

<next_action>
## Next Steps

1. **Start Phase 3 (Sub-PRD 03)** — Options Relaxation. Read `03-options-relaxation.md` first. Build with nullable warnings as errors during this phase to catch every NPE site mechanically — known affected sites are `ServiceCollectionExtensions.cs` lines 28, 33-37, 62-64 and (after Phase 2) re-validate `MarkdownSyncService.cs`.
2. Phase 3 is independent of Phase 2 conceptually but the codebase has changed; re-run `grep -n` for `options.Embedding\|options.ConnectionString\|options.Llm` to enumerate touch sites before editing.
3. Phases 4 and 5 are sequential and depend on 2 + 3.
4. **PRD-test-path drift continues**: every remaining sub-PRD says `tests/Minerva.UnitTests/...`. Disk has `tests/Minerva.Tests/`. Translate paths.
</next_action>

<key_files>
## Key Files

- Master PRD: `.dev/preflight-and-manager/00-master-plan.md`
- Phase 3 PRD: `.dev/preflight-and-manager/03-options-relaxation.md`
- Design brief: `.dev/preflight-and-manager/2026-04-21-preflight-and-manager.md`
- Modified this session:
  - `src/Minerva/Ingestion/IEmbeddingDimensionProvider.cs` (new)
  - `src/Minerva/Providers/OpenAICompatibleEmbeddingProvider.cs` (implements provider; internal `IEmbeddingProbeFacade` seam)
  - `src/Minerva/DI/ServiceCollectionExtensions.cs` (concrete-singleton + forwarding registrations)
  - `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs` (injects `IEmbeddingDimensionProvider`; dead injection removed)
  - `tests/Minerva.Tests/Providers/EmbeddingDimensionProviderTests.cs` (new, 6 tests)
- Phase-3 touch-points to verify: `src/Minerva/DI/ServiceCollectionExtensions.cs:28,33-37,62-64`
</key_files>

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

Resume Phase 3 (Sub-PRD 03 — Options Relaxation) of the preflight-and-manager feature. Read `.dev/preflight-and-manager/03-options-relaxation.md` first, then enumerate NPE sites with `grep -n 'options\.\(Embedding\|ConnectionString\|Llm\)' src/`. Build with nullable warnings as errors to catch every site mechanically. Phase 3 is independent of Phase 2; Phase 4 and 5 depend on 3. Translate `tests/Minerva.UnitTests/...` paths in any sub-PRD to `tests/Minerva.Tests/...`.
