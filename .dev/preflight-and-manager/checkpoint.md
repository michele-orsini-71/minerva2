---
branch: main
last_commit: 45d411a updates dev-plan status
uncommitted_changes: true
checkpointed: '2026-04-27T07:30:48.953Z'
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

**Goal**: Add a uniform `IReadinessCheck` pipeline to the Minerva core library so any client (today: `Minerva.MarkdownWatcher`; tomorrow: `minerva doctor` CLI / orchestrator GUI) can probe Postgres, pgvector, embedder, LLM, watched-folder, and embedding-dimension consistency *before* `host.RunAsync()`.

**Phases 1–4 — DONE** in prior sessions and committed.
**Phase 5 (Sub-PRD 05, Watcher Checks + Program.cs) — code-complete this session, uncommitted**: three watcher checks (`RootPathExistsCheck` / `CollectionNameValidCheck` / `CollectionDimensionMatchCheck`) implemented under `src/Minerva.MarkdownWatcher/Readiness/`; registered unconditionally in `AddMinervaWatcher()` per Decision 5; `Program.cs` rewritten as `try`/`catch` returning `int` (0=ready+ran, 1=bootstrap exception, 2=preflight failed) with the explicit pre-host preflight scope from the PRD; `Minerva.IntegrationTests.csproj` gained a `ProjectReference` to `Minerva.MarkdownWatcher` so the integration test can instantiate the public check.

**Verification (all green)**: `dotnet build Minerva.sln` clean (0 warnings, 0 errors); `dotnet test tests/Minerva.Tests` 201 passed (+26 new: 5 root-path + 13 collection-name + 8 dimension-match); `dotnet test tests/Minerva.IntegrationTests` 22 passed (+3 new for `CollectionDimensionMatchCheck` branches 2/3/4); `dotnet test tests/Minerva.ArchitectureTests` 13 passed (unchanged). 17/18 master-plan steps marked done via `status-update`; `gate-check` reports `atGate: false` because Phase 5 / Step 4 (manual end-to-end exit-code verification) is intentionally left open for the user.
</context>

<current_state>
## Current Progress

- ✅ Phase 1: Readiness Core (4/4)
- ✅ Phase 2: Embedding Dimension Provider (3/3)
- ✅ Phase 3: Options Relaxation (3/3)
- ✅ Phase 4: Built-in Library Checks (4/4)
- 🟡 Phase 5: Watcher Checks and Program.cs (3/4) — code complete; manual smoke remaining

**Overall**: 17/18 (94%)
</current_state>

<next_action>
## Next Steps

1. **Run the three manual end-to-end smokes** (Phase 5 / Step 4 — the only thing between code-complete and feature-complete):
   - **Healthy**: `dotnet run --project src/Minerva.MarkdownWatcher` against your live stack — host starts and runs as before; Ctrl-C exits 0.
   - **Misconfigured**: `Minerva__ConnectionString="Host=localhost;Port=1;..." Watcher__RootPath=/tmp Watcher__CollectionName=smoke dotnet run --project src/Minerva.MarkdownWatcher` — exits with `$? == 2`; logs contain a `LogError` per failed check with `Code` / `Message` / `Remediation`; verify password literal `'p'` does NOT appear in any logged `Message` (Redact assertion). This is the highest-value smoke since it proves the new exit-2 contract.
   - **Probe-skipped warning**: temporarily comment out the `using (var scope = ...)` preflight block in `Program.cs`, run with full healthy config — the `MinervaStartupService` safety-net `LogWarning` fires once at `Warning` level. Revert the edit afterward.
2. **First env-prefixed `dotnet` invocation will validate the permissions cleanup** — `.claude/settings.local.json` lost 4 exact-match entries and gained `Bash(Minerva__*)` / `Bash(MINERVA_*)` / `Bash(Watcher__*)`. If the smoke #2 invocation prompts again, the matcher does not treat `*` as a greedy glob across `=` / quotes / spaces; fall back to `Bash(env *)` + force `env KEY=val cmd` shape, or a wrapper script.
3. **After all three smokes pass**: run `node "$CLI" status-update --phase 5 --step 4 --marker done --dir .dev/preflight-and-manager` — that closes Phase 5 and `gate-check` will report `allComplete: true`. Then commit (user handles git per memory) and merge.
</next_action>

<key_files>
## Key Files

- Master PRD: `.dev/preflight-and-manager/00-master-plan.md`
- Phase 5 PRD: `.dev/preflight-and-manager/05-watcher-checks-and-program.md`
- New checks: `src/Minerva.MarkdownWatcher/Readiness/RootPathExistsCheck.cs`, `CollectionNameValidCheck.cs`, `CollectionDimensionMatchCheck.cs`
- Modified: `src/Minerva.MarkdownWatcher/Program.cs`, `src/Minerva.MarkdownWatcher/DI/ServiceCollectionExtensions.cs`, `tests/Minerva.IntegrationTests/Minerva.IntegrationTests.csproj`
- New tests: `tests/Minerva.Tests/Watcher/Readiness/{RootPathExists,CollectionNameValid,CollectionDimensionMatch}CheckTests.cs`, `tests/Minerva.IntegrationTests/Readiness/CollectionDimensionMatchCheckIntegrationTests.cs`
- Pattern templates (already-shipped library checks for reference): `src/Minerva/Readiness/Checks/{PostgresConnectivity,PgVectorExtension,EmbeddingCall,LlmCall}Check.cs`
</key_files>

<decisions>
- Watcher unit tests placed under `tests/Minerva.Tests/Watcher/Readiness/` (translated from PRD's `tests/Minerva.MarkdownWatcher.UnitTests/`). The `Minerva.Tests` project already references `Minerva.MarkdownWatcher` and is `InternalsVisibleTo`'d from the watcher — a new test project would be redundant. Honors prior-session translation rule.
- `CollectionNameValidCheck` uses plain compiled `Regex` (`RegexOptions.Compiled`) instead of `[GeneratedRegex]` partial methods. Reason: matches Phase 1's `Redact` decision and avoids needing a `partial` class for a single regex. The two existing collection-name regex sites (`CollectionManager.cs:84`, `SchemaInitializer.cs:106`) intentionally diverge in style — three copies kept in sync manually per design.
- `CollectionDimensionMatchCheck` uses an internal `ICollectionDimensionProbe` seam (same pattern as Phase 4's `IPostgresConnectivityProbe` / `IPgVectorExtensionProbe`). The probe internally catches `PostgresException` SQLSTATE `42P01` (table missing) and returns `null`, so branches 2 (table-missing) and 3 (no-row) collapse to the same observable result at the check level: `MINERVA.CLIENT.DIMENSION_OK`. Integration tests distinguish them via real DB state.
- `RootPathExistsCheck` uses an internal `IRootPathProbe` seam with `DirectoryExists(string)` + `EnumerateTopLevel(string)`. The unauthorized-access branch is unit-testable without `chmod` games on darwin temp dirs.
- Integration test branch 2 (collections-table missing) uses a separate `NpgsqlDataSource` with `Options=-c search_path=pg_temp` rather than dropping the shared `collections` table. Non-destructive — no inter-test ordering issues with the `StorageCollection` xunit fixture.
- `tests/Minerva.IntegrationTests/Minerva.IntegrationTests.csproj` gained a `ProjectReference` to `Minerva.MarkdownWatcher`. New cross-cutting reference; previously the integration project referenced core `Minerva` only.
- `Program.cs` is `try`/`catch` returning `int`: 0=ready+ran, 1=unhandled bootstrap exception, 2=preflight not ready. `using Microsoft.Extensions.Configuration;` added explicitly (LSP needed it for `Bind` on `IConfigurationSection`).
- `AddMinervaReadinessCheck<T>` registrations live in `AddMinervaWatcher()` (NOT `AddMinerva()`) — Decision 5: watcher checks are watcher-specific.
- All four `CollectionDimensionMatchCheck` short-circuit conditions use `serviceProvider.GetService<>()` resolution inside the ctor (parallel to Phase 4 library checks): `MinervaOptions.ConnectionString == null`, `MinervaOptions.Embedding == null`, `IEmbeddingDimensionProvider == null`, OR `NpgsqlDataSource == null` → pass with `MINERVA.CLIENT.DIMENSION_NOT_CONFIGURED`. Phase 3's conditional registration may leave any of these unregistered; constructor injection would throw at activation.
</decisions>

<notes>
- Manual end-to-end smoke is the only remaining gate. Three scenarios per the PRD checklist, none reproducible by `dotnet test`: healthy (exit 0), misconfigured Postgres (exit 2 + structured report + redacted password), probe-skipped warning (one-time `LogWarning` from `MinervaStartupService` when preflight is skipped despite a registered marker).
- `dotnet test` final counts: 201 unit (was 175, +26), 22 integration (was 19, +3), 13 architecture (unchanged). `dotnet build Minerva.sln`: 0 warnings / 0 errors.
- Permissions cleanup this session: `.claude/settings.local.json` had 4 exact-match `MINERVA_TEST_CONNSTRING=...` / `Minerva__ConnectionString=...` Bash entries (lines 16, 17, 27, 29 of the pre-edit file) — each one bound to a literal command string, so every new env-var-prefixed `dotnet` invocation prompted. Replaced with three prefix patterns: `Bash(Minerva__*)`, `Bash(MINERVA_*)`, `Bash(Watcher__*)`. **Untested** — the next env-prefixed `dotnet` invocation will reveal whether the Bash matcher treats `*` as a true greedy glob across `=`, quoted strings, and intervening spaces. If it prompts: fall back to `Bash(env *)` + force `env KEY=val cmd` shape, or wrapper script.
- `Minerva.MarkdownWatcher` has no architecture-test net for ring violations (Phase 4's session note carried over) — verified by hand that `Minerva.MarkdownWatcher.Readiness.*` only depends on `Minerva.Configuration` (options), `Minerva.Ingestion` (ports), `Minerva.Readiness` (contracts), `Npgsql` (adapter), and watcher-local options.
- PRD-test-path drift continues: PRD 05 references `tests/Minerva.MarkdownWatcher.UnitTests/Readiness/` and `tests/Minerva.UnitTests/`. Disk reality is `tests/Minerva.Tests/Watcher/Readiness/`. Translated paths per the prior-session rule; do not rename the test project.
- After the three smokes pass, the closing step is `node "$CLI" status-update --phase 5 --step 4 --marker done --dir .dev/preflight-and-manager`, which flips `gate-check.allComplete` to `true`. User handles the commit (memory).
</notes>

---

Run the three Phase 5 manual end-to-end smokes from `.dev/preflight-and-manager/05-watcher-checks-and-program.md` Step 4: (1) healthy → exit 0, (2) misconfigured Postgres → exit 2 + structured report (verify password literal does NOT appear in logged Message), (3) probe-skipped warning by commenting out the preflight scope in `Program.cs`. After all three pass, mark Phase 5 / Step 4 done via `status-update` to close the feature.
