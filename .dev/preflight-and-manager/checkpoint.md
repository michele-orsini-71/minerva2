---
branch: main
last_commit: '3e98002 preflight and manager plan: phase 2: Embedding Dimension Provider'
uncommitted_changes: true
checkpointed: '2026-04-26T14:19:37.731Z'
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

**Phase 1 (Readiness Core, Sub-PRD 01) — DONE** in earlier session.
**Phase 2 (Embedding Dimension Provider, Sub-PRD 02) — DONE** in prior session and committed (`3e98002`).
**Phase 3 (Options Relaxation, Sub-PRD 03) — DONE this session, uncommitted**: `MinervaOptions.ConnectionString` and `MinervaOptions.Embedding` are now nullable (joining `Llm`); `AddMinerva()` registers storage / embedding / LLM / engine blocks conditionally on the corresponding options being non-null; `MarkdownSyncService.EnsureCollectionAsync` throws `ConfigurationException` if `Embedding` is null. Six new DI tests cover no-config, storage-only, embedding-only, LLM-only, storage+embedding, and full-config builds.

**Verification (all green)**: `dotnet build Minerva.sln` clean (0 warnings, 0 errors); `dotnet test tests/Minerva.Tests` 147 passed (+6 new); `dotnet test tests/Minerva.ArchitectureTests` 12 passed; `dotnet test tests/Minerva.IntegrationTests` 16 passed (auto-wired via `.runsettings` against local Postgres).
</context>

<current_state>
## Current Progress

- ✅ Phase 1: Readiness Core (Sub-PRD 01)
- ✅ Phase 2: Embedding Dimension Provider (Sub-PRD 02) — committed at 3e98002
- ✅ Phase 3: Options Relaxation (Sub-PRD 03) — uncommitted on main
- ⬜ Phase 4: Built-in Library Checks (Sub-PRD 04)
- ⬜ Phase 5: Watcher Checks and Program.cs (Sub-PRD 05)

Overall: 10/18 (56%).
</current_state>

<next_action>
## Next Steps

1. **Commit Phase 3** — uncommitted files are: `MinervaOptions.cs`, `ServiceCollectionExtensions.cs`, `MarkdownSyncService.cs`, the new `tests/Minerva.Tests/DI/AddMinervaConditionalRegistrationTests.cs`, and the master-plan / sub-PRD 03 status updates.
2. **Start Phase 4 (Sub-PRD 04, Built-in Library Checks)** — read `04-builtin-library-checks.md` first. This phase adds `OpenAICompatibleLlmProvider.CheckAvailabilityAsync()` (one-shot, bypass Polly + RateLimiter, `MaxOutputTokenCount = 5`, HTTP 400 → passes) and the five library checks (`ConnectionStringParseCheck`, `PostgresConnectivityCheck`, `PgVectorExtensionCheck`, `EmbeddingCallCheck`, `LlmCallCheck`).
3. Reuse the Polly-bypass discipline from Phase 2 — call `_client` directly, also skipping `_rateLimiter`. The dimension probe in `OpenAICompatibleEmbeddingProvider` is the reference implementation.
4. Each library check must short-circuit when its config block is null (now achievable thanks to Phase 3 relaxation).
5. Register the checks from `AddMinerva()`, gated on the same conditions used in `ServiceCollectionExtensions.cs` from this session.
6. **PRD-test-path drift continues**: Phase 4 PRD says `tests/Minerva.UnitTests/...`; disk has `tests/Minerva.Tests/`. Translate paths.
7. **Use `status-update --phase --step --marker done`** for progress markers from now on — manual ✅ edits in markdown do not satisfy the parser used by `progress-summary` / `gate-check`.
</next_action>

<key_files>
## Key Files

- Master PRD: `.dev/preflight-and-manager/00-master-plan.md`
- Phase 4 PRD: `.dev/preflight-and-manager/04-builtin-library-checks.md`
- Design brief (non-negotiable inputs): `.dev/preflight-and-manager/2026-04-21-preflight-and-manager.md`
- DI registration (now conditional): `src/Minerva/DI/ServiceCollectionExtensions.cs`
- Options (now nullable): `src/Minerva/Configuration/MinervaOptions.cs`
- Watcher guard added: `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs:82-105`
- New tests: `tests/Minerva.Tests/DI/AddMinervaConditionalRegistrationTests.cs`
- Polly-bypass reference (for Phase 4 LLM check): `src/Minerva/Embedding/OpenAICompatibleEmbeddingProvider.cs` — `ProbeDimensionCoreAsync` / `IEmbeddingProbeFacade`
- Existing LLM provider (Phase 4 will add `CheckAvailabilityAsync` here): `src/Minerva/Llm/OpenAICompatibleLlmProvider.cs`
- Architecture-test rules (extend for any new namespace): `tests/Minerva.ArchitectureTests/LayerDependencyTests.cs`
- Integration-test fixture (reuse for Postgres-touching checks): `tests/Minerva.IntegrationTests/Storage/StorageTestFixture.cs`
</key_files>

<decisions>
- `AddMinerva()` eagerly evaluates the user's `Action<MinervaOptions>` once at the top of the method to drive registration-time conditionals (`var options = new MinervaOptions(); configure(options);`). It then ALSO calls `services.Configure(configure)` so `IOptions<MinervaOptions>` is available at resolution time. Both calls are necessary — the eager one gates DI registration; the IOptions one feeds runtime factories.
- Four conditional registration blocks, not three: storage (`ConnectionString != null`), embedding (`Embedding != null`), LLM (`Llm != null`), and `ConnectionString != null && Embedding != null` (which gates `IngestionPipeline` and `IMinervaEngine`, since both require services from both feature blocks).
- `ProviderFactory` and `IDocumentChunker` remain unconditional — they have no feature-block dependency. `IDocumentChunker` only consumes `MinervaOptions.Chunking`, which is non-nullable.
- `MarkdownSyncService.EnsureCollectionAsync` reads `_minervaOptions.Embedding` once and throws `ConfigurationException` if null, with a message pointing at the preflight invariant. The throw is defensive — once Phase 5 lands, preflight blocks startup before this code runs — but it documents the dependency and keeps the watcher's contract explicit.
- PRD instruction 'build with nullable warnings as errors during this PRD' was already in effect via `Directory.Build.props` (global `<Nullable>enable</Nullable>` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`). No per-project or per-phase toggle was added.
- Test file path: `tests/Minerva.Tests/DI/AddMinervaConditionalRegistrationTests.cs` — PRD said `tests/Minerva.UnitTests/DI/...`. Honoring prior session's translation rule.
- `Trait("Category", "DI")` for the new test class, parallel to the existing `Trait("Category", "Readiness")` and `Trait("Category", "Providers")` conventions.
</decisions>

<notes>
- After Step 1 (`MinervaOptions` relaxation) only TWO nullability errors surfaced, not the four the PRD predicted: `NpgsqlDataSourceBuilder(string?)` accepts a nullable string, so the `options.ConnectionString` dereference on line 28 of `ServiceCollectionExtensions.cs` did not fail compilation. It still needed wrapping in the conditional block — for runtime correctness (no-config containers were building bogus data sources), not for build success. The MarkdownWatcher dereference also did not surface in the first build because the core project failed first.
- The `progress-summary` / `gate-check` CLI parsers do NOT recognize manually-edited `✅` markers in master-plan step lists. Updating the markdown by hand left `progress-summary` reporting Phase 3 as 0/3 not-started. The `status-update --phase N --step M --marker done` CLI is the only supported writer — it produces a different on-disk shape than my hand edit. For Phase 4+, skip the manual edit entirely.
- Integration tests auto-wire `MINERVA_TEST_CONNSTRING` via `tests/Minerva.IntegrationTests/.runsettings` (declared in the `<RunConfiguration><EnvironmentVariables>` block) and `Directory.Build.props:9` (`<RunSettingsFilePath Condition="Exists('$(MSBuildProjectDirectory)\.runsettings')">...`). `dotnet test tests/Minerva.IntegrationTests` Just Works against a local Postgres at `localhost:5432` with database `minerva_test` and role `michele`. No shell export needed.
- LSP false-positive: `using Minerva.Models;` in `AddMinervaConditionalRegistrationTests.cs` flagged as CS8019 (unnecessary), but removing it actually breaks compilation because `ProviderOptions` lives there. The build (with TreatWarningsAsErrors) succeeds with the using in place, so the LSP analyzer disagrees with the compiler. Trust the compiler.
- User preference (saved to memory): no XML-doc `///` comments on options/config classes — Clean Code self-documenting style. When I added prose summaries to the relaxed `MinervaOptions` properties, the user removed them.
- Phase 4 entry: the LLM `CheckAvailabilityAsync` Polly+RateLimiter bypass should mirror `OpenAICompatibleEmbeddingProvider`'s `IEmbeddingProbeFacade` pattern from Phase 2 — internal facade interface, default SDK-wrapping impl, internal ctor for test override. The OpenAI SDK `ChatClient` is also sealed (mirrors `EmbeddingClient`).
</notes>

---

Resume the Minerva preflight feature at Phase 4 (Built-in Library Checks). Phase 3 is complete and uncommitted on `main`. First commit Phase 3, then read `.dev/preflight-and-manager/04-builtin-library-checks.md`. Phase 4 adds `OpenAICompatibleLlmProvider.CheckAvailabilityAsync()` (Polly+RateLimiter bypass, mirror Phase 2's `IEmbeddingProbeFacade` pattern) plus five library checks: `ConnectionStringParseCheck`, `PostgresConnectivityCheck`, `PgVectorExtensionCheck`, `EmbeddingCallCheck`, `LlmCallCheck`. Register them from `AddMinerva()` gated on the same conditions established in this session. Use `status-update --phase N --step M --marker done` for progress markers — manual ✅ edits do not satisfy the parser. Translate `tests/Minerva.UnitTests/...` paths to `tests/Minerva.Tests/...`.
