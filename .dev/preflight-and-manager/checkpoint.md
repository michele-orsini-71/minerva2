---
branch: main
last_commit: 1f297c1 implements all minerva library checks
uncommitted_changes: false
checkpointed: '2026-04-26T16:17:03.987Z'
---
Read the following PRD files in order:

1. 00-master-plan.md
2. 01-readiness-core.md
3. 02-embedding-dimension-provider.md
4. 03-options-relaxation.md
5. 04-builtin-library-checks.md
6. 05-watcher-checks-program.md

<context>
## Context

**Goal**: Add a uniform `IReadinessCheck` pipeline to the Minerva core library so any client (today: `Minerva.MarkdownWatcher`; tomorrow: `minerva doctor` CLI / orchestrator GUI) can probe Postgres, pgvector, embedder, LLM, watched-folder, and embedding-dimension consistency *before* `host.RunAsync()`.

**Phases 1–3 — DONE** (prior sessions; committed).
**Phase 4 (Sub-PRD 04, Built-in Library Checks) — DONE this session and committed (`1f297c1`)**: `OpenAICompatibleLlmProvider.CheckAvailabilityAsync` (Polly + RateLimiter bypass via internal `IChatClientFacade`, HTTP 400 → reachable), new `ILlmAvailabilityProbe` port, five library checks (`ConnectionStringParseCheck`, `PostgresConnectivityCheck`, `PgVectorExtensionCheck`, `EmbeddingCallCheck`, `LlmCallCheck`) with per-check timeouts (2s/5s/30s) and short-circuit branches, registered unconditionally from `AddMinerva()`. DI now registers `OpenAICompatibleLlmProvider` as concrete singleton with forwarding to both `ILlmClient` and `ILlmAvailabilityProbe` (symmetric with Phase 2's embedding pattern). Architecture tests rescoped: `Minerva.Readiness` rule now exact-match (`ResideInNamespaceMatching(@"^Minerva\.Readiness$")`) and a new `ReadinessChecks_DoesNotDependOnDi` rule places `Minerva.Readiness.Checks` in the adapter ring (allowed to consume Npgsql, OpenAI SDK, and `Minerva.Configuration.MinervaOptions`).

**Verification (all green)**: `dotnet build Minerva.sln` clean (0 warnings, 0 errors); `dotnet test tests/Minerva.Tests` 175 passed (28 new); `dotnet test tests/Minerva.IntegrationTests` 19 passed (3 new); `dotnet test tests/Minerva.ArchitectureTests` 13 passed (1 new). `gate-check` reports `atGate: true` for Phase 4.
</context>

<current_state>
## Current Progress

- ✅ Phase 1: Readiness Core (Sub-PRD 01) — 4/4
- ✅ Phase 2: Embedding Dimension Provider (Sub-PRD 02) — 3/3
- ✅ Phase 3: Options Relaxation (Sub-PRD 03) — 3/3
- ✅ Phase 4: Built-in Library Checks (Sub-PRD 04) — 4/4 — ⏸️ **AT GATE**
- ⬜ Phase 5: Watcher Checks and Program.cs (Sub-PRD 05) — 0/4

**Overall**: 14/18 steps (78%).
</current_state>

<next_action>
## Next Steps

1. **Start Phase 5 (Sub-PRD 05, Watcher Checks + Program.cs)** — read `05-watcher-checks-program.md` first. This phase adds the watcher-side checks (`WatchedFolderExistsCheck`, `WatchedFolderReadableCheck`, `CollectionNameValidCheck`, `CollectionDimensionMatchCheck`) and rewires `Minerva.MarkdownWatcher/Program.cs` to invoke `IReadinessChecker.CheckReadinessAsync()` before `host.RunAsync()`, with exit code 2 on failure and a formatted report via `ReadinessReportFormatter`.
2. The `CollectionDimensionMatchCheck` reuses the cache populated by `EmbeddingCallCheck` via `IEmbeddingDimensionProvider.GetDimensionAsync` — no extra round trip.
3. The `^[a-zA-Z0-9][a-zA-Z0-9-]*` collection-name regex now exists in `CollectionManager.cs:84` and `SchemaInitializer.cs:106` (DDL-injection guard); the new `CollectionNameValidCheck` will be the third instance — keep all three in sync manually (per design, not centralized).
4. Watcher checks are registered ONLY in `Minerva.MarkdownWatcher/Program.cs`, NOT in `AddMinerva()` (they are watcher-specific, not library-level). Use `services.AddMinervaReadinessCheck<...>()` extension.
5. Architecture tests cover the `Minerva` assembly only — `Minerva.MarkdownWatcher` has no arch-test net for ring violations; verify by hand.
6. **PRD-test-path drift continues**: Phase 5 PRD will say `tests/Minerva.UnitTests/...`; disk has `tests/Minerva.Tests/`. Translate paths.
7. **Use `status-update --phase --step --marker done`** for master-plan markers; the sub-PRD step table can be hand-edited (gate-check only reads master plan).
</next_action>

<key_files>
## Key Files

### PRDs
- Master plan: `.dev/preflight-and-manager/00-master-plan.md`
- Phase 5 sub-PRD: `.dev/preflight-and-manager/05-watcher-checks-program.md`
- Design brief (non-negotiable inputs): `.dev/preflight-and-manager/2026-04-21-preflight-and-manager.md`

### Phase-4 outputs (this session)
- `src/Minerva/Ingestion/ILlmAvailabilityProbe.cs` (new port)
- `src/Minerva/Providers/OpenAICompatibleLlmProvider.cs` (added `CheckAvailabilityAsync`, internal `IChatClientFacade` seam, implements `ILlmAvailabilityProbe`)
- `src/Minerva/Readiness/Checks/{ConnectionStringParse,PostgresConnectivity,PgVectorExtension,EmbeddingCall,LlmCall}Check.cs` (5 new checks)
- `src/Minerva/DI/ServiceCollectionExtensions.cs` (concrete-LLM-singleton + forwarding; unconditional readiness registrations)
- `tests/Minerva.Tests/Readiness/Checks/*.cs` (5 unit-test files, 28 new tests including 400-passes branch and Polly-bypass)
- `tests/Minerva.IntegrationTests/Readiness/{PostgresConnectivity,PgVectorExtension}CheckIntegrationTests.cs` (3 live-DB tests)
- `tests/Minerva.ArchitectureTests/LayerDependencyTests.cs` (rescoped Readiness rule + new ReadinessChecks rule)

### Phase-5 entry points
- Watcher: `src/Minerva.MarkdownWatcher/Program.cs` (host wiring), `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs`
- Pre-existing collection-name regex sites to keep in sync: `src/Minerva/Collections/CollectionManager.cs:84`, `src/Minerva/Storage/SchemaInitializer.cs:106`
- Reference patterns: `src/Minerva/Readiness/Checks/EmbeddingCallCheck.cs` (cache-reuse pattern via `IEmbeddingDimensionProvider`); `src/Minerva/Readiness/ReadinessReportFormatter.cs` (output format for the watcher's preflight report)
</key_files>

<decisions>
- Internal probe-interface seam pattern reused for every SDK-touching check (`IPostgresConnectivityProbe`, `IPgVectorExtensionProbe`, `IChatClientFacade`) — mirrors Phase 2's `IEmbeddingProbeFacade`. Default impls are `private sealed` nested classes wrapping `NpgsqlDataSource` / `OAI.ChatClient`; tests inject fakes via `internal` ctors.
- `OpenAICompatibleLlmProvider` now implements `ILlmAvailabilityProbe` (third interface alongside `IChatClient` + `ILlmClient`). DI registers concrete singleton with forwarding registrations for both `ILlmClient` and `ILlmAvailabilityProbe`, fully symmetric with Phase 2's embedding-side pattern (concrete + two forwardings). Verified single-instance via DI activation tests.
- `ILlmAvailabilityProbe` port lives in `src/Minerva/Ingestion/` (alongside `ILlmClient`, `IEmbeddingDimensionProvider`, `IEmbeddingClient`) — NOT in `Minerva.Readiness`. Reason: matches the established port-namespace convention; avoids registering a new namespace in `NamespaceCoverageTests.ClassifiedNamespaces`.
- `Minerva.Readiness.Checks` placed in the adapter ring. Existing `Readiness_DoesNotDependOnAdaptersOrFramework` arch rule rescoped from `ResideInNamespace("Minerva.Readiness")` (which is a starts-with match in NetArchTest) to `ResideInNamespaceMatching(@"^Minerva\.Readiness$")` so it no longer captures `.Checks`. New `ReadinessChecks_DoesNotDependOnDi` rule forbids only `Minerva.DI` — checks legitimately need `Minerva.Configuration.MinervaOptions` for feature-off short-circuiting, so the Configuration-ring restriction is intentionally relaxed for this adapter only.
- Optional services (`NpgsqlDataSource`, `IEmbeddingDimensionProvider`, `ILlmAvailabilityProbe`) are resolved via `IServiceProvider.GetService<>()` inside the check constructors (not direct constructor injection). Reason: Phase-3 conditional registration may leave any feature block unregistered; constructor injection of an unregistered type would throw at DI activation, before the check could short-circuit.
- Postgres SQLSTATE-to-Code mapping (3D000 → DATABASE_MISSING, 28P01 → AUTH_FAILED, NpgsqlException/TimeoutException → UNREACHABLE) is unit-tested by fabricating `PostgresException` via its public ctor `(messageText, severity, invariantSeverity, sqlState)` — no DB needed. Network/unreachable branch is also covered live in integration tests against `localhost:1`.
- `ClientResultException` 400-passes test (the LLM-probe key invariant) constructs the exception via the 3-arg ctor `(message, PipelineResponse, innerException)` with a hand-rolled `FakePipelineResponse : System.ClientModel.Primitives.PipelineResponse` and nested `EmptyHeaders : PipelineResponseHeaders` — the abstract pipeline types have no public test helpers.
- Test paths translated to `tests/Minerva.Tests/Readiness/Checks/` (PRD said `tests/Minerva.UnitTests/...`). Honoring prior-session translation rule.
- `status-update --phase --step --marker done` updates only the master plan (which `gate-check`/`progress-summary` consume). The sub-PRD's own step table was hand-edited (`⬜ Not Started` → `✅ Done`) for human readability; this is fine because the parser does not read sub-PRD step tables.
</decisions>

<notes>
- `Minerva.Readiness.Checks` as a sub-namespace is treated by `NamespaceCoverageTests.TopLevel()` as `Minerva.Readiness` (truncates after second segment), so the existing USE_CASES classification in `ClassifiedNamespaces` still satisfies the coverage test. The layer-model comment in `LayerDependencyTests.cs` documents `Minerva.Readiness.Checks` separately as ADAPTERS for human readers.
- `PostgresException` has a public ctor `(string messageText, string severity, string invariantSeverity, string sqlState)` in Npgsql 10.0.2 — confirmed via reflection probe. This is what makes the SQLSTATE unit tests possible without a live DB.
- Integration tests for `PostgresConnectivityCheck` cover both the live-pass branch (against `StorageTestFixture.DataSource`) and the unreachable-fail branch (creating an ad-hoc `NpgsqlDataSource` against `localhost:1` with `Timeout=2;Command Timeout=2`). The unreachable branch is what proves the host:port substitution in the remediation actually works on real `NpgsqlException` messages.
- Phase 5 entry: the watcher's preflight invocation needs to mark the `IReadinessProbeMarker` (already wired in Phase 1) and exit with code 2 on `IsReady = false`. The `ReadinessReportFormatter` from Phase 1 is the output formatter; do not duplicate its logic in `Program.cs`.
- User feedback this session (saved to memory): when bash commands address paths inside the workspace, prefer relative paths (e.g. `src/Minerva/...`) over absolute (`/Users/michele/my-code/minerva2/src/Minerva/...`) — absolute paths under the workspace trigger permission prompts on every invocation. Reserve absolute paths for genuinely external locations (`~/.nuget`, `~/.claude`, `/tmp`).
</notes>

---

Resume Phase 5 of preflight-and-manager: read `.dev/preflight-and-manager/05-watcher-checks-program.md`, then implement the watcher-side checks (WatchedFolderExistsCheck, WatchedFolderReadableCheck, CollectionNameValidCheck, CollectionDimensionMatchCheck) and rewire `Minerva.MarkdownWatcher/Program.cs` to invoke IReadinessChecker.CheckReadinessAsync() before host.RunAsync() with exit code 2 on failure. Phase 4 (library checks) is committed at 1f297c1 and gate-check confirms atGate=true. Translate `tests/Minerva.UnitTests/...` PRD paths to `tests/Minerva.Tests/...` as before.
