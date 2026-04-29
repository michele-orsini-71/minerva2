---
slug: 2026-04-21-preflight-and-manager
created: 2026-04-21T05:49:41Z
last_updated: 2026-04-25T06:06:11Z
status: finalized
---

# Minerva Preflight Readiness Checks

## Goal

Add a preflight readiness-check system to the Minerva core library so that
clients (starting with `Minerva.MarkdownWatcher`) can detect and report
missing or misconfigured prerequisites — Postgres unreachable, database
missing, `pgvector` extension absent, embedding/LLM endpoint unreachable,
configured model not available at the provider, watched folder missing,
embedding-dimension mismatch against an existing collection — **before**
the host starts, and surface them as a structured report with actionable
remediation hints.

The current behaviour is a hard crash on first bad assumption (observed:
`MinervaStartupService.StartAsync()` → `SchemaInitializer.InitializeAsync()`
throws `NpgsqlException` when Postgres is unreachable, aborting the
process). The preflight system replaces that with a uniform, explicit,
pre-host readiness probe.

## Constraints and Non-Goals

- **Out of scope — orchestration / lifecycle UI.** A GUI that fires, configures,
  and monitors multiple watchers is deferred. Preflight is solved as a
  library-level capability so any future client (CLI `doctor`, orchestrator,
  GUI) can reuse it.
- **Out of scope — detecting models currently loaded in RAM.** Whether
  LM Studio has a model loaded in memory, or whether Ollama has
  `OLLAMA_KEEP_ALIVE` / `OLLAMA_MAX_LOADED_MODELS` set appropriately, is
  not detectable cheaply and reliably. Document the performance footgun
  in each client's README; do not try to detect it.
- **Out of scope — auto-rebuild on embedder change.** If the configured
  embedder produces vectors of a different dimension than an existing
  collection, the check fails with clear remediation. The user decides
  what to do (drop and rebuild, revert embedder). Automatic rebuild
  logic, and broader feature parity with original-Minerva extractor
  behaviour, is deferred.
- **Out of scope — schema validation.** Schema is created by
  `SchemaInitializer` on first run; the preflight only verifies the
  database exists, not its schema state.
- **Only one client (MarkdownWatcher) is shipped here.** The library API is
  designed to support future clients (CLI `doctor`, orchestrator, GUI), but
  none beyond MarkdownWatcher are in scope for this feature. MarkdownWatcher
  also functions as the validation vehicle for the library's preflight API.

## Decisions

### 1. Unified `IReadinessCheck` pipeline

**Choice**: A single pluggable interface `IReadinessCheck`; both the library
and clients register checks into one shared registry; a single
`IReadinessChecker` service enumerates and runs them.

**Rationale**: One surface, uniform UX, uniform report. The moment there is
a second client, the pattern pays off with zero additional design. Avoids
the parallel-checks-in-every-client boilerplate of the alternative.

### 2. Dual API surface: probe + build

**Choice**: Ship both a dedicated `IReadinessChecker.CheckReadinessAsync()`
(side-effect-light probe) *and* keep the existing `BuildEngine()` / host
startup semantics. Clients explicitly call the probe *before* starting the
host.

**Rationale**: A probe that does not require building the engine enables
a future `minerva doctor` CLI and keeps the watcher startup explicit.
`BuildEngine()` stays unchanged so we do not disrupt existing call sites.

### 3. Preflight runs before `Host.RunAsync()`

**Choice**: The client's `Program.cs` builds the host, opens a DI scope,
resolves `IReadinessChecker`, runs the report, and either exits cleanly
(status 0 with the report logged) or proceeds to `await host.RunAsync()`.
The library's `MinervaStartupService` is **not** refactored to run
preflight — keeping the library unopinionated about hosting.

**Rationale**: Explicit in the client is obvious, trivially readable,
unopinionated about host lifecycle, and identical for a future CLI
(same call, no host involved).

**Interaction with `MinervaStartupService`**: `MinervaStartupService`
(and therefore `SchemaInitializer`) is **not** modified to *run*
preflight. After a successful preflight, the host starts as it does
today; schema init succeeds because Postgres connectivity and the
`pgvector` extension have just been validated. Clients that forget
to call the probe retain today's behaviour (hard crash inside the
hosted service).

A small log-only safety net is added: `MinervaStartupService` checks
a flag set by `IReadinessChecker.CheckReadinessAsync()` (e.g. an
internal `IReadinessProbeMarker` singleton flipped on first
successful probe). If the flag is unset on `StartAsync`, the service
emits a `LogWarning` ("Minerva preflight was not invoked before host
start; failures will surface as runtime exceptions"). No behaviour
change, no startup gate — purely an observability hook for the
explicit-by-design trade-off.

### 4. Provider availability check via direct call (not model listing)

**Choice**: For embedder and LLM providers, perform a **real call**
against the configured endpoint with the configured model — a tiny
embedding request (e.g., input `"preflight"`) for the embedder, a
minimal completion (e.g., prompt `"ping"`, with a small `max_tokens`
cap — single-digit, exact value is not load-bearing) for the LLM.
Endpoint reachability, authentication, and model availability are
all validated in one step: if the direct call succeeds, the provider
is ready; if it fails, the provider's error tells us why.

**Rationale**: A direct call is the most authoritative possible
check — "can I actually embed with this model right now?" — and it
sidesteps three problems that a listing-endpoint approach would
carry: (1) restricted API keys that return 403 on `/v1/models`, (2)
the need for an Ollama-vs-OpenAI endpoint discriminator, (3)
model-name normalisation (`llama3:latest` vs `llama3`, case,
tag suffixes). Cost is one tiny call per provider per startup —
negligible even on paid APIs. (Dimension reuse between preflight
and runtime is handled by the memoized accessor introduced in
Decision 12, not by threading the dimension between checks.)
Whether a model is currently hot-loaded in RAM is still not
something we claim to detect (see non-goals); a direct call will
simply fail or time out, which is the behaviour we want.

### 5. No severity field — configuration-driven check execution

**Choice**: No `Severity` enum on checks or results. Every registered
check is must-pass for the report to be ready. Because `MinervaOptions`
is not bound at DI registration time, checks are **registered
unconditionally** by `AddMinerva()` / `AddMinervaWatcher()`; each
check resolves `IOptions<MinervaOptions>` (and, for watcher checks,
`IOptions<WatcherOptions>`) at `RunAsync` time and **short-circuits
to a trivially-passing result** when its feature is not configured.

To make the short-circuit pattern uniform (rather than only applicable
to `Llm`, the one currently-nullable property), `MinervaOptions` is
relaxed: `Embedding` and `ConnectionString` lose their `required`
modifier and become nullable alongside `Llm`. Missing config ⇒
feature off ⇒ trivially-passing check. Validation of
configured-but-malformed sub-fields is intentionally not in scope
(see Approach Preferences); incoherent options for a feature that
will not be exercised cost nothing.

**Rationale**: A severity levelling system invites policy questions
("should this warning block startup?") that have no universal answer.
Registering-all-checks-and-short-circuiting-at-runtime achieves the
same "only meaningful checks affect readiness" outcome while avoiding
the DI-timing trap (options are not yet bound when `AddMinerva()`
runs). If a check is in the registry and does not short-circuit, it is
fatal-in-context by construction.

### 6. Remediation string as a first-class result field

**Choice**: Every `ReadinessCheckResult` carries a `Remediation` string
alongside `Message`. Writing a good remediation hint is part of each
check's job, not a downstream concern.

**Rationale**: Transforms the report from "here is what is broken" into
"here is what is broken and here is what to do". Doubles the UX value
for trivial additional cost.

### 7. Result / interface shape

**Choice**:

```csharp
public enum ReadinessCategory { Storage, Embedding, Llm, Client, Configuration }

public record ReadinessCheckResult(
    string Name,
    ReadinessCategory Category,
    bool Passed,
    string Code,           // stable machine-readable identity; grammar: UPPER_SNAKE segments separated by '.', first segment "MINERVA", second segment category short-name, third segment failure mode (e.g. "MINERVA.POSTGRES.CONNECTION_FAILED")
    string? Message,
    string? Remediation);

public record ReadinessReport(IReadOnlyList<ReadinessCheckResult> Results)
{
    public bool IsReady => Results.All(r => r.Passed);
}

public interface IReadinessCheck
{
    string Name { get; }
    ReadinessCategory Category { get; }
    Task<ReadinessCheckResult> RunAsync(CancellationToken ct);
}

public interface IReadinessChecker
{
    Task<ReadinessReport> CheckReadinessAsync(CancellationToken ct = default);
}
```

Registration via a DI extension: `services.AddMinervaReadinessCheck<T>()`.
Library calls it internally from `AddMinerva()` for its own checks.
Clients call it from their own registration (e.g. `AddMinervaWatcher()`).
Checks are registered with **`Transient`** lifetime (they hold no
state across runs and may capture per-invocation scope through their
dependencies).

**Rationale**: Records are value-type-friendly for tests and logging;
the interface is minimal; `ReadinessCategory` stays a closed enum
because `Client` already covers any non-library concern a client would
raise. `Code` is the only machine-readable field on the result —
`Duration` and `CheckedAt` are deferred (not valuable for a one-shot
startup probe; can be added later if a dashboard or long-running
doctor mode materialises).

### 8. No ergonomic host-extension helper

**Choice**: Do not ship a `host.RunWithReadinessCheckAsync()` helper.
Clients write the ~10 lines explicitly in `Program.cs`.

**Rationale**: The explicit form is obvious and self-documenting.
A helper hides behaviour for a marginal brevity gain.

### 9. Built-in library checks

**Choice**: `AddMinerva()` registers the following unconditionally;
each check short-circuits to a passing result when its feature is not
configured (per Decision 5). The short-circuit branches are:

- `ConnectionStringParseCheck` — pure, no I/O; validates the Npgsql
  connection string is syntactically valid. Short-circuits to pass
  when `options.ConnectionString == null`.
- `PostgresConnectivityCheck` — opens a connection; validates server
  reachability, auth, and that the named database exists (catches
  SQLSTATE `3D000` → remediation: `CREATE DATABASE <name>`).
  Short-circuits to pass when `options.ConnectionString == null`.
- `PgVectorExtensionCheck` — queries `pg_extension` in the active
  database first (is `vector` already enabled here?); on miss, falls
  back to `pg_available_extensions` to distinguish **"extension not
  installed on the server"** (remediation: install pgvector; e.g.
  `apt install postgresql-16-pgvector`) from **"extension installed
  but not enabled in this database"** (remediation: `CREATE
  EXTENSION vector` — note this may require superuser; on managed
  Postgres, check the provider's docs for enabling pgvector). Note:
  the schema initializer also runs `CREATE EXTENSION` via migration,
  but we prefer to surface the distinction at preflight rather than
  rely on an opaque migration failure. Short-circuits to pass when
  `options.ConnectionString == null`.
- `EmbeddingCallCheck` — calls
  `IEmbeddingDimensionProvider.GetDimensionAsync()` (Decision 12),
  which on first invocation performs a tiny real embedding call
  (input `"preflight"`) against the configured embedder. Success ⇒
  endpoint reachable, auth OK, model available, model servable, and
  the dimension is now memoized on the provider for all downstream
  consumers (`CollectionDimensionMatchCheck`, the watcher's
  collection-creation path). Short-circuits to pass when
  `options.Embedding == null`.
- `LlmCallCheck` — performs a minimal completion call (prompt `"ping"`,
  small single-digit `max_tokens` cap) against the configured LLM.
  Short-circuits to pass when `options.Llm == null`.

All network-touching checks (Postgres, Embedding, LLM) bypass the
provider's retry/resilience pipeline (Decision 11) and use a direct
one-shot path with a per-check timeout; a retrying probe would stall
startup needlessly on dead endpoints.

### 10. Watcher-specific checks

**Choice**: `AddMinervaWatcher()` registers:

- `RootPathExistsCheck` — the watched directory exists and is readable.
- `CollectionNameValidCheck` — matches the existing
  `^[a-zA-Z0-9][a-zA-Z0-9-]*$` regex (currently validated at runtime
  inside `CollectionManager.ValidateName`; lift to preflight).
- `CollectionDimensionMatchCheck` — lives on the watcher (not the
  library) because `CollectionName` is a `WatcherOptions` property,
  not a `MinervaOptions` property. Branches are evaluated in this
  explicit order:
    1. **Embedder unavailable** — if calling
       `IEmbeddingDimensionProvider.GetDimensionAsync()` raises (the
       cached failure from a prior failed `EmbeddingCallCheck`),
       short-circuit to a passing result with message
       `skipped: embedder unavailable`. The failure is already
       reported upstream; repeating it adds noise.
    2. **Collections table missing** — if the Postgres query raises
       SQLSTATE `42P01` (fresh DB, schema not yet initialised),
       pass trivially.
    3. **No row matching configured name** — pass trivially (the
       collection will be created on first run).
    4. **Comparison** — verify the row's stored `embedding_dimension`
       equals the cached dimension from `GetDimensionAsync()`. On
       mismatch, fail with remediation:
       `"Configured embedder produces N dimensions; existing
       collection '<name>' uses M. Either revert the embedder change,
       or drop the collection and its data
       (DELETE FROM collections WHERE name='<name>') and let it
       rebuild on next run."`
  Hits the cache populated by `EmbeddingCallCheck` — no second
  network call.

### 11. Sequential execution, per-check timeout, no retry

**Choice**: `IReadinessChecker` runs checks sequentially in
registration order. Each check runs under a **per-check-class
timeout**, implemented via a linked `CancellationTokenSource`, and
**bypasses the provider's Polly resilience pipeline** — the embedding
and LLM probes issue a one-shot call, not a retrying call. Defaults:

- **Local / pure / fast**: `ConnectionStringParseCheck`,
  `RootPathExistsCheck`, `CollectionNameValidCheck` — **2 s**.
- **Local DB**: `PostgresConnectivityCheck`,
  `PgVectorExtensionCheck`, `CollectionDimensionMatchCheck` — **5 s**.
- **Remote model providers**: `EmbeddingCallCheck`, `LlmCallCheck`
  — **30 s**. Local LLM/embedding stacks (Ollama, LM Studio) can
  take meaningful time on a cold load; the per-check timeout must
  not turn cold-loaded-but-healthy into "embedder unavailable",
  which would directly contradict the non-goal of not detecting
  hot-loaded-in-RAM state. The cold-start performance footgun is
  documented in the README task in Open Questions; the timeout is
  generous enough to ride out a cold load.

**Rationale**: Deterministic output and uninterleaved logs. Retrying
makes preflight worse, not better: a dead endpoint would stall the
probe for ~7 s+ per provider under the existing resilience settings
(3 retries with exponential backoff), and the *point* of preflight
is to fail fast and report. Sequential ordering with class-tiered
timeouts keeps worst-case total preflight time bounded (~70 s with
all checks active) while not punishing a cold-loading local model.
Easy to revisit if a specific check needs a different budget.

### 12. Embedding dimension exposed as a memoized provider accessor

**Choice**: Introduce a Minerva-owned interface
`IEmbeddingDimensionProvider` with a single method:

```csharp
public interface IEmbeddingDimensionProvider
{
    Task<int> GetDimensionAsync(CancellationToken ct = default);
}
```

`OpenAICompatibleEmbeddingProvider` implements it (alongside its
existing `IEmbeddingGenerator<string, Embedding<float>>` and
`IEmbeddingClient` implementations). The first call performs a tiny
real embedding request (input `"preflight"`) — issued one-shot,
**bypassing the provider's Polly resilience pipeline**, consistent
with Decision 11's no-retry stance for preflight. The dimension is
cached via `Lazy<Task<int>>` so concurrent first-callers cannot fan
out into two network calls. Failures are **cached as failures** —
subsequent callers within the same process re-throw the original
exception immediately rather than re-attempting (rationale below).

**Cancellation discipline (use this `Lazy<Task<int>>` shape)**: the
factory lambda invokes the underlying probe with
`CancellationToken.None` so that no individual caller's cancellation
poisons the cached task; each caller composes its own CT via
`_lazy.Value.WaitAsync(ct)` so the *await* is cancellable while the
underlying task continues. The wrong shape — capturing the first
caller's CT in the factory — would let a transient first-caller
timeout (e.g. preflight's 30 s cap firing during a cold model load)
cache a `TaskCanceledException` permanently, breaking the runtime
path forever. Implementers must avoid that shape.

Every consumer of the dimension — `EmbeddingCallCheck`,
`CollectionDimensionMatchCheck`, and the watcher's collection-
creation path in `MarkdownSyncService` — calls this single method.
The existing `MarkdownSyncService.ProbeEmbeddingDimensionAsync()`
is deleted; its caller switches to
`IEmbeddingDimensionProvider.GetDimensionAsync()`.

**Rationale**: Dimension is an intrinsic property of the configured
embedder model, not a value callers should rediscover on their own.
Without this accessor, preflight and runtime both need the same
number and we'd be stuck with either a duplicate embedding call
(wasteful) or threading the preflight result into runtime via
shared mutable state (spooky side effect). A memoized accessor is
neither — it is an explicit, queryable property that happens to be
resolved lazily. A dedicated single-method interface (rather than
extending `IEmbeddingClient`) keeps the dimension concern isolated
from the "generate embeddings" concern (ISP) and gives test fakes
a one-method surface.

**Failure-caching policy**: Cache failures, do not retry. The full
call pattern is one preflight call plus, at most, one runtime call
within the same process startup window. If the embedder is broken,
preflight reports it and the process exits non-zero; recovery is a
process restart, supervised by systemd / Docker / CI. There is no
long-running flow within a single process that would benefit from
in-process self-healing. Fail-fast on the second call also keeps
log noise down. (A future `minerva doctor` mode or a retry policy
on `GetDimensionAsync` are noted as deferred — not part of this
feature.)

**Why a cached failure never harms the runtime path**: the runtime
embedding/collection-creation flow is reached only *after*
`Program.cs` confirms `report.IsReady`. If `EmbeddingCallCheck`
failed, the process exits with code 2 and runtime is never invoked.
The runtime call to `GetDimensionAsync()` therefore always observes
a successfully-cached dimension — there is no in-process scenario
in which a runtime caller awakens a poisoned cache.

**Scope note**: This crosses the preflight/core-library boundary —
it adds an interface to `Minerva` and modifies the embedding
provider. It is small (one interface, one lazy field, one method,
deletion of one existing probe) and it eliminates the design
tension at the source rather than working around it. Treated as
part of this feature, not a separate refactor.

## Approach Preferences

- **Pragmatic Clean Architecture** — no zealotry. Follow the project's
  existing conventions (e.g. exception hierarchy rooted at
  `MinervaException`, DI-based wiring, `IOptions<T>` for configuration).
- **Provider-level probe methods stay in the provider classes**, not in
  parallel probe classes. For the LLM, add `CheckAvailabilityAsync()`
  directly to `OpenAICompatibleLlmProvider`. For the embedder,
  availability is probed via `IEmbeddingDimensionProvider.GetDimensionAsync()`
  (Decision 12), implemented on `OpenAICompatibleEmbeddingProvider`.
  Readiness checks delegate to these provider methods. Keeps
  provider-specific HTTP concerns inside the provider.
- **Remediation strings are part of the check's responsibility.** Every
  check must produce a remediation that tells a human what to change or
  run. Generic "something went wrong" text is a bug.
- **Checks translate thrown exceptions into failed results, with
  redaction.** A check never propagates exceptions; `try`/`catch`
  around the probe. The raw exception is logged at `Debug` level
  (full fidelity for diagnosis); the result's `Message` is a short
  sanitised summary — connection-string fragments (`Password=…`),
  API keys, and bearer tokens are stripped before they reach the
  `Message` field or the default log output. Stable identity is
  provided by `Code`; the human-readable `Message` is free-text but
  safe-to-log; the hand-authored `Remediation` tells the user what
  to change.
- **Embedder dimension is a property of the embedder, not a value
  callers rediscover.** Treat `IEmbeddingDimensionProvider.GetDimensionAsync()`
  (Decision 12) as the canonical source. Never probe the embedder
  ad-hoc for its dimension elsewhere in the codebase; if a new
  consumer needs the dimension, it injects the same interface.
- **Short-circuit-what-does-not-apply at run time.** Checks are
  registered unconditionally (options are not bound when
  `AddMinerva()` runs); each check reads `IOptions<MinervaOptions>`
  in `RunAsync` and returns a passing result if its feature is not
  configured. Same end-user effect as register-what-applies, but
  mechanically correct with the existing DI pipeline.
- **Permissive options shape, no proactive validation of unused
  config.** `MinervaOptions` properties are nullable; missing config
  ⇒ feature off ⇒ check trivially passes. We deliberately do **not**
  validate that a non-null options block has all required sub-fields
  before its feature check runs — incoherent options for a feature
  that will not be exercised cost nothing and the lazy approach
  keeps the check pipeline simple. If the feature *is* used, the
  feature check itself fails with a meaningful message.
- **The watcher's `Program.cs` pattern** — explicit pre-host snippet,
  non-zero exit on failure so CI / systemd / orchestrators see a
  real failure:
  ```csharp
  var host = builder.Build();
  using (var scope = host.Services.CreateScope())
  {
      var checker  = scope.ServiceProvider.GetRequiredService<IReadinessChecker>();
      var logger   = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
      var report   = await checker.CheckReadinessAsync();

      // Each failing check: LogError with Name/Code/Message/Remediation.
      // One LogInformation summary line either way.
      ReadinessReportFormatter.LogReport(logger, report);

      if (!report.IsReady)
      {
          return 2;  // 0 = ready, 2 = not ready, 1 = unexpected error (uncaught)
      }
  }
  await host.RunAsync();
  return 0;
  ```
  A small `ReadinessReportFormatter` helper ships with the library so
  clients produce uniform output; clients remain free to format
  themselves if they want custom UX.

## Open Questions

- [ ] **Minerva-feature-parity review.** The original Minerva (pre-minerva2)
      shipped a range of CLI extractors, commodities, and behaviours
      (e.g. on-embedder-change full rebuild semantics). A systematic
      inventory and decision on which of those carry forward into
      minerva2 is deferred. Revisit when preflight is landed and the
      second client is scoped.
- [ ] **README documentation of the Ollama / LM Studio performance
      footgun.** Write a short note per client: for local providers, keep
      both embedder and LLM resident (Ollama: `OLLAMA_MAX_LOADED_MODELS`
      ≥ 2 and `OLLAMA_KEEP_ALIVE`; LM Studio: load both models in the
      Models panel). Not a check; a documentation task.
- [ ] **Retry policy for `IEmbeddingDimensionProvider.GetDimensionAsync()`.**
      Decision 12 caches failures and does not retry — adequate for the
      one-shot startup pattern. If a future use case appears that
      benefits from in-process self-healing (e.g. a long-running
      `minerva doctor` mode, or a watcher that should survive transient
      embedder outages), revisit and add a retry policy or a manual
      `Reset()` hook. Tracked here so it does not get lost.

## Research Findings

Findings below were gathered from a focused subagent investigation of
the current minerva2 codebase. They ground the design above and identify
the integration points where the preflight system attaches.

- **Crash path confirmed.** The reported crash originates in
  `Minerva/DI/MinervaStartupService.StartAsync()` →
  `Minerva/Storage/SchemaInitializer.InitializeAsync()` →
  `_dataSource.OpenConnectionAsync(ct)`. This is a hosted service running
  inside `Host.RunAsync()`, so failures escape as unhandled exceptions
  and abort the process. Preflight attaches **before** this, in the
  client's `Program.cs`.

- **No existing health-check or validation infrastructure.** No
  `IHealthCheck`, no ASP.NET HealthChecks registration, no
  `IValidateOptions<T>`, no FluentValidation. Validation today is
  scattered (`CredentialResolver`, `SchemaInitializer`,
  `CollectionManager`, `MarkdownSyncService`, `IngestionPipeline`).
  Preflight is new infrastructure, but can centralise several of these
  scattered checks over time.

- **Provider abstractions suit the plan.** Embedding uses
  `IEmbeddingGenerator<string, Embedding<float>>` (Microsoft.Extensions.AI);
  LLM uses `IChatClient`. Both wrap the OpenAI SDK in
  `OpenAICompatibleEmbeddingProvider` / `OpenAICompatibleLlmProvider`
  via `ProviderFactory`. Neither currently exposes an availability probe;
  both are created lazily (no HTTP at construction). Adding
  `CheckAvailabilityAsync()` to the concrete provider classes is the
  cleanest insertion point.

- **No `IExtractor` interface exists.** `MarkdownScanner` is referenced
  directly; watcher-specific checks (RootPath exists, CollectionName
  valid) are currently inlined in `MarkdownSyncService.ExecuteAsync()`.
  The unified check pipeline does **not** require introducing a full
  extractor abstraction — it only needs the lightweight `IReadinessCheck`
  interface plus per-check classes that a client registers. The lifting
  of existing inlined checks into dedicated `IReadinessCheck`
  implementations is part of the work.

- **Eager singleton factory for `NpgsqlDataSource` is not a problem.**
  The factory executes at DI registration but does not open a connection;
  connection opens happen lazily in `SchemaInitializer`. So registering
  and running preflight from the same DI container is safe.

- **Runtime dimension probing already exists in the watcher, and is
  the only runtime consumer.** `MarkdownSyncService.ProbeEmbeddingDimensionAsync()`
  (`src/Minerva.MarkdownWatcher/MarkdownSyncService.cs:101`) is the
  sole runtime path that discovers the dimension; its result flows
  into `CollectionManager.CreateAsync(..., embeddingDimension, ...)`
  and `SchemaInitializer.EnsureHnswIndexAsync(..., dimension, ...)`,
  is persisted on the `Collection` row (`Collection.EmbeddingDimension`),
  and is read back from Postgres thereafter — no other runtime
  path re-probes. Adding preflight introduces a second consumer
  (`EmbeddingCallCheck` + `CollectionDimensionMatchCheck`); without
  intervention this would mean either a duplicate embedding call or
  hidden state-passing between checks and runtime. Decision 12
  resolves this by lifting the dimension into a memoized accessor
  (`IEmbeddingDimensionProvider.GetDimensionAsync()`) that all three
  callers share, and deleting `ProbeEmbeddingDimensionAsync()`.

- **`MinervaException` hierarchy is in place.** All custom exceptions
  (`ConfigurationException`, `EmbeddingException`,
  `ProviderUnavailableException`, …) descend from `MinervaException`
  (`/Minerva/Exceptions/MinervaException.cs`). Checks can catch
  `MinervaException` generically for translation, while still falling
  through for unexpected exceptions.

- **`MinervaOptions` shape will be relaxed by this feature.** Today,
  `options.Llm` is nullable while `options.Embedding` and
  `options.ConnectionString` are `required`. Decision 5 makes
  `Embedding` and `ConnectionString` nullable too, so the
  short-circuit-when-feature-not-configured pattern applies uniformly
  to every check. Missing config ⇒ feature off, not a binding error.
  The change is local to the options class and the call sites that
  treat these properties as non-null (which now must null-check or
  fail with a feature-specific message at use-time).
