<!-- markdownlint-disable MD033 -->
<!-- markdownlint-disable MD041 -->

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

## Session 4 — 2026-04-26T14:19:37.731Z

<context>
## Context

**Goal**: Add a uniform `IReadinessCheck` pipeline to the Minerva core library so any client (today: `Minerva.MarkdownWatcher`; tomorrow: `minerva doctor` CLI / orchestrator GUI) can probe Postgres, pgvector, embedder, LLM, watched-folder, and embedding-dimension consistency *before* `host.RunAsync()`.

**Phase 1 (Readiness Core, Sub-PRD 01) — DONE** in earlier session.
**Phase 2 (Embedding Dimension Provider, Sub-PRD 02) — DONE** in prior session and committed (`3e98002`).
**Phase 3 (Options Relaxation, Sub-PRD 03) — DONE this session, uncommitted**: `MinervaOptions.ConnectionString` and `MinervaOptions.Embedding` are now nullable (joining `Llm`); `AddMinerva()` registers storage / embedding / LLM / engine blocks conditionally on the corresponding options being non-null; `MarkdownSyncService.EnsureCollectionAsync` throws `ConfigurationException` if `Embedding` is null. Six new DI tests cover no-config, storage-only, embedding-only, LLM-only, storage+embedding, and full-config builds.

**Verification (all green)**: `dotnet build Minerva.sln` clean (0 warnings, 0 errors); `dotnet test tests/Minerva.Tests` 147 passed (+6 new); `dotnet test tests/Minerva.ArchitectureTests` 12 passed; `dotnet test tests/Minerva.IntegrationTests` 16 passed (auto-wired via `.runsettings` against local Postgres).
</context>

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

## Session 5 — 2026-04-26T16:17:03.987Z

<context>
## Context

**Goal**: Add a uniform `IReadinessCheck` pipeline to the Minerva core library so any client (today: `Minerva.MarkdownWatcher`; tomorrow: `minerva doctor` CLI / orchestrator GUI) can probe Postgres, pgvector, embedder, LLM, watched-folder, and embedding-dimension consistency *before* `host.RunAsync()`.

**Phases 1–3 — DONE** (prior sessions; committed).
**Phase 4 (Sub-PRD 04, Built-in Library Checks) — DONE this session and committed (`1f297c1`)**: `OpenAICompatibleLlmProvider.CheckAvailabilityAsync` (Polly + RateLimiter bypass via internal `IChatClientFacade`, HTTP 400 → reachable), new `ILlmAvailabilityProbe` port, five library checks (`ConnectionStringParseCheck`, `PostgresConnectivityCheck`, `PgVectorExtensionCheck`, `EmbeddingCallCheck`, `LlmCallCheck`) with per-check timeouts (2s/5s/30s) and short-circuit branches, registered unconditionally from `AddMinerva()`. DI now registers `OpenAICompatibleLlmProvider` as concrete singleton with forwarding to both `ILlmClient` and `ILlmAvailabilityProbe` (symmetric with Phase 2's embedding pattern). Architecture tests rescoped: `Minerva.Readiness` rule now exact-match (`ResideInNamespaceMatching(@"^Minerva\.Readiness$")`) and a new `ReadinessChecks_DoesNotDependOnDi` rule places `Minerva.Readiness.Checks` in the adapter ring (allowed to consume Npgsql, OpenAI SDK, and `Minerva.Configuration.MinervaOptions`).

**Verification (all green)**: `dotnet build Minerva.sln` clean (0 warnings, 0 errors); `dotnet test tests/Minerva.Tests` 175 passed (28 new); `dotnet test tests/Minerva.IntegrationTests` 19 passed (3 new); `dotnet test tests/Minerva.ArchitectureTests` 13 passed (1 new). `gate-check` reports `atGate: true` for Phase 4.
</context>

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
