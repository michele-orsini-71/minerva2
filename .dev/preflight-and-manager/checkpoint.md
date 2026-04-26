---
branch: main
last_commit: 1e1f3f0 changes preflight and manager plan
uncommitted_changes: true
checkpointed: '2026-04-26T07:01:30.750Z'
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

**Goal**: Add a uniform `IReadinessCheck` pipeline to the Minerva core library so any client (today: `Minerva.MarkdownWatcher`; tomorrow: a `minerva doctor` CLI / orchestrator GUI) can probe Postgres, pgvector, embedder, LLM, watched-folder, and embedding-dimension consistency *before* `host.RunAsync()`.

**Phase 1 (Readiness Core, Sub-PRD 01) — DONE**: contracts, sequential `ReadinessChecker` with per-check timeout, `Redact` helper, `ReadinessProbeMarker`, `ReadinessReportFormatter`, DI extension, and a safety-net warning in `MinervaStartupService` when the marker exists but `Probed == false`. Architecture tests updated for the new `Minerva.Readiness` use-case-ring partition.

**Verification (all green this session)**: `dotnet build Minerva.sln` clean; `dotnet test Minerva.Tests --filter Category=Readiness` 17 passed; `dotnet test Minerva.ArchitectureTests` 12 passed (including the new `Readiness_DoesNotDependOnAdaptersOrFramework` rule and `NamespaceCoverageTests` classification).
</context>

<current_state>
## Current Progress

- ✅ Phase 1 (Sub-PRD 01) — Readiness Core (4/4 steps; gate reached)
- ⬜ Phase 2 (Sub-PRD 02) — Embedding Dimension Provider (0/3)
- ⬜ Phase 3 (Sub-PRD 03) — Options Relaxation (0/3)
- ⬜ Phase 4 (Sub-PRD 04) — Built-in Library Checks (0/4)
- ⬜ Phase 5 (Sub-PRD 05) — Watcher Checks and Program.cs (0/4)

Overall: 4/18 (22%). Phases 2 and 3 are independent and may be done in either order or in parallel after Phase 1.
</current_state>

<next_action>
## Next Steps

1. **Start Phase 2 (Sub-PRD 02)** — Embedding Dimension Provider. First step: introduce `IEmbeddingDimensionProvider` and implement on `OpenAICompatibleEmbeddingProvider` with a `Lazy<Task<int>>` (factory uses `CT.None`; callers compose via `_lazy.Value.WaitAsync(ct)`). Bypass Polly + RateLimiter on the SDK call. See `02-embedding-dimension-provider.md` Step 1.
2. After Phase 2 lands, also do Phase 3 (Options Relaxation) — these are independent of each other.
3. Phases 4 and 5 are sequential and depend on 2 + 3.
4. **PRD-test-path drift**: every remaining sub-PRD says `tests/Minerva.UnitTests/...`. The actual project on disk is `tests/Minerva.Tests/`. Translate paths when implementing.
</next_action>

<key_files>
## Key Files

- Master PRD: `.dev/preflight-and-manager/00-master-plan.md`
- Phase 1 (done): `.dev/preflight-and-manager/01-readiness-core.md`
- Phase 2 (next): `.dev/preflight-and-manager/02-embedding-dimension-provider.md`
- Design brief (reference, do not modify): `.dev/preflight-and-manager/2026-04-21-preflight-and-manager.md`
- Readiness contracts/impl shipped this session: `src/Minerva/Readiness/*.cs`
- Modified safety-net: `src/Minerva/DI/MinervaStartupService.cs`
- Architecture tests updated: `tests/Minerva.ArchitectureTests/LayerDependencyTests.cs`, `tests/Minerva.ArchitectureTests/NamespaceCoverageTests.cs`
- Phase-1 tests added: `tests/Minerva.Tests/Readiness/`, `tests/Minerva.Tests/DI/MinervaStartupServiceWarningTests.cs`
- Reference for Phase 2: `src/Minerva/Providers/OpenAICompatibleEmbeddingProvider.cs` (Polly site at line 44), `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs` (`ProbeEmbeddingDimensionAsync` to delete; latent `IEmbeddingGenerator` injection bug)
</key_files>

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

Resume the preflight-and-manager feature. Phase 1 (Readiness Core) is complete and at the gate. Begin Phase 2 (Sub-PRD 02 — Embedding Dimension Provider) starting with Step 1: add `IEmbeddingDimensionProvider` and implement on `OpenAICompatibleEmbeddingProvider` with a `Lazy<Task<int>>` (factory uses `CT.None`; callers compose via `_lazy.Value.WaitAsync(ct)`). The SDK call must bypass both Polly and the RateLimiter. Translate any `tests/Minerva.UnitTests/...` paths in the PRD to `tests/Minerva.Tests/...`.
