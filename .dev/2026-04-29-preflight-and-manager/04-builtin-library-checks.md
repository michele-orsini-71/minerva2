# Sub-PRD: Built-in Library Checks

**Parent**: [00-master-plan.md](./00-master-plan.md)
**Status**: Done
**Dependency**: [02-embedding-dimension-provider.md](./02-embedding-dimension-provider.md), [03-options-relaxation.md](./03-options-relaxation.md)
**Last Updated**: 2026-04-26

---

## Implementation Progress

| Step | Description | Status |
| ------ | ------------- | -------- |
| **1** | `OpenAICompatibleLlmProvider.CheckAvailabilityAsync` | ✅ Done |
| **2** | Five library checks with timeouts and short-circuit branches | ✅ Done |
| **3** | Register from `AddMinerva()` | ✅ Done |
| **4** | Unit + integration tests | ✅ Done |

---

## Goal

Implement the five library-level readiness checks; add `OpenAICompatibleLlmProvider.CheckAvailabilityAsync()` (one-shot, bypassing Polly + the `RateLimiter`, `MaxOutputTokenCount = 5`, HTTP 400 → passes); register all five from `AddMinerva()`. After this PRD, calling `IReadinessChecker.CheckReadinessAsync()` against a healthy environment returns `IsReady = true`; against a misconfigured one, returns descriptive failures with remediation.

---

## Implementation Steps

### Step 1: `OpenAICompatibleLlmProvider.CheckAvailabilityAsync`

**File**: `src/Minerva/Llm/OpenAICompatibleLlmProvider.cs`

Add a public method:

```csharp
public async Task CheckAvailabilityAsync(CancellationToken ct)
{
    var options = new ChatCompletionOptions { MaxOutputTokenCount = 5 };
    var messages = new[] { new UserChatMessage("ping") };
    try
    {
        await _client.CompleteChatAsync(messages, options, ct);
    }
    catch (ClientResultException ex) when (ex.Status == 400)
    {
        // A reasoning model rejecting our probe params is still demonstrably reachable.
        return;
    }
    // Other exceptions propagate; LlmCallCheck translates.
}
```

**Critical**: this method calls `_client` directly — it must **not** route through `_resiliencePipeline` (no retries on a dead endpoint) and must **not** acquire the `RateLimiter` slot (no reason to block preflight on concurrency limits). The existing public methods (`GetResponseAsync`, etc.) wrap calls in both — `CheckAvailabilityAsync` does neither.

**Test seam**: extract a thin internal `IChatClientFacade { Task CompleteChatAsync(IList<ChatMessage> messages, ChatCompletionOptions options, CancellationToken ct); }` consumed only by `CheckAvailabilityAsync`. The default impl wraps `_client.CompleteChatAsync`. Tests inject a fake to exercise the 400-passes branch.

### Step 2: Five library checks

All checks live under `src/Minerva/Readiness/Checks/`. Each implements `IReadinessCheck` and the optional `IReadinessCheckTimeout` interface (defined in PRD 01). Each check:
- Reads `IOptions<MinervaOptions>` to detect feature-off and short-circuit.
- Catches all exceptions, redacts `ex.Message` via `Redact.Apply`, emits a failed result with a stable `Code` and a hand-authored `Remediation`.
- Logs the raw exception at `LogDebug` for diagnosis.

**Per-check timeouts** (per Decision 11):
- Local / pure / fast: **2 s**
- Local DB: **5 s**
- Remote model providers: **30 s** (rides out cold loads of Ollama / LM Studio)

#### `ConnectionStringParseCheck`

- **File**: `src/Minerva/Readiness/Checks/ConnectionStringParseCheck.cs`
- **Category**: `Configuration`. **Timeout**: 2s.
- Short-circuit: `options.ConnectionString == null` → trivially passing.
- Else: `new NpgsqlConnectionStringBuilder(options.ConnectionString)`. Pass on no throw.
- On `ArgumentException`: fail with `Code = "MINERVA.CONFIG.CONN_STRING_INVALID"`, `Message = Redact.Apply(ex.Message)`, `Remediation = "Check the Minerva:ConnectionString format. Expected: 'Host=...;Database=...;Username=...;Password=...'."`.

#### `PostgresConnectivityCheck`

- **File**: `src/Minerva/Readiness/Checks/PostgresConnectivityCheck.cs`
- **Category**: `Storage`. **Timeout**: 5s.
- Constructor injects `IOptions<MinervaOptions>` and `NpgsqlDataSource?` (resolved via `IServiceProvider.GetService` — may be null if storage is unconfigured per PRD 03).
- Short-circuit: `options.ConnectionString == null` → trivially passing.
- Else: `await using var conn = await dataSource!.OpenConnectionAsync(ct);` — pass on success.
- Translate `PostgresException`:
  - SQLSTATE `3D000` → `Code = "MINERVA.POSTGRES.DATABASE_MISSING"`, remediation `"Database '<name>' does not exist. Run: CREATE DATABASE \"<name>\";"`.
  - SQLSTATE `28P01` → `Code = "MINERVA.POSTGRES.AUTH_FAILED"`, remediation `"Postgres rejected the credentials. Verify the username and password in Minerva:ConnectionString."`.
  - Other (network / `08*` / generic `NpgsqlException`) → `Code = "MINERVA.POSTGRES.UNREACHABLE"`, remediation `"Postgres at '<host>:<port>' did not respond. Verify the server is running and reachable from this host."`.

#### `PgVectorExtensionCheck`

- **File**: `src/Minerva/Readiness/Checks/PgVectorExtensionCheck.cs`
- **Category**: `Storage`. **Timeout**: 5s.
- Constructor injects `IOptions<MinervaOptions>` and `NpgsqlDataSource?` (optional).
- Short-circuit: `options.ConnectionString == null` → trivially passing.
- First query: `SELECT 1 FROM pg_extension WHERE extname = 'vector'`. If returns 1 → pass.
- Else, second query: `SELECT 1 FROM pg_available_extensions WHERE name = 'vector'`. If returns 1 → fail with `Code = "MINERVA.PGVECTOR.NOT_ENABLED"`, remediation `"pgvector is installed on the server but not enabled in this database. Run: CREATE EXTENSION vector; (may require superuser; on managed Postgres see your provider's docs for enabling pgvector)."`.
- Else (returns 0) → fail with `Code = "MINERVA.PGVECTOR.NOT_AVAILABLE"`, remediation `"pgvector is either not installed on the server, OR your role cannot see pg_available_extensions (Azure Database for PostgreSQL / RDS may hide this catalog from non-admin roles). Install pgvector via your distro (e.g. apt install postgresql-16-pgvector) or check your managed-Postgres provider's portal/CLI."`.
- Defensive: connection failures here defer to `PostgresConnectivityCheck`'s message; `Remediation = "Postgres is not reachable; see PostgresConnectivityCheck for diagnosis."`.

#### `EmbeddingCallCheck`

- **File**: `src/Minerva/Readiness/Checks/EmbeddingCallCheck.cs`
- **Category**: `Embedding`. **Timeout**: 30s.
- Constructor injects `IOptions<MinervaOptions>` and `IEmbeddingDimensionProvider?` (optional — null when embedding is unconfigured per PRD 03).
- Short-circuit: `options.Embedding == null` → trivially passing.
- Else: `await dimensionProvider!.GetDimensionAsync(ct)` — pass on success. This populates the cache for `CollectionDimensionMatchCheck` in PRD 05.
- On `ProviderUnavailableException` or generic exception: fail with `Code = "MINERVA.EMBEDDING.UNAVAILABLE"`, redacted message, remediation `"Embedder at '<endpoint>' did not respond, or the model '<model>' is not available. Verify (1) the endpoint URL, (2) the API key, (3) that the model name matches what the provider exposes. For local stacks (Ollama / LM Studio), confirm the model is loaded — see the README for the keep-loaded settings."`.

#### `LlmCallCheck`

- **File**: `src/Minerva/Readiness/Checks/LlmCallCheck.cs`
- **Category**: `Llm`. **Timeout**: 30s.
- Constructor injects `IOptions<MinervaOptions>` and an `OpenAICompatibleLlmProvider?` (or, preferred: a thin `ILlmAvailabilityProbe` interface for symmetry with the embedding side).
- Short-circuit: `options.Llm == null` → trivially passing.
- Else: `await provider!.CheckAvailabilityAsync(ct)` — pass on success.
- On exception: fail with `Code = "MINERVA.LLM.UNAVAILABLE"`, redacted message, remediation `"LLM at '<endpoint>' did not respond, or the model '<model>' is not available. Verify (1) the endpoint URL, (2) the API key, (3) that the model name matches what the provider exposes."`.

### Step 3: Register from `AddMinerva()`

**File**: `src/Minerva/DI/ServiceCollectionExtensions.cs`

After PRD 03's conditional blocks, register the readiness core and the five checks **unconditionally** (per Decision 5 — checks short-circuit at runtime):

```csharp
services.AddMinervaReadinessCore();
services.AddMinervaReadinessCheck<ConnectionStringParseCheck>();
services.AddMinervaReadinessCheck<PostgresConnectivityCheck>();
services.AddMinervaReadinessCheck<PgVectorExtensionCheck>();
services.AddMinervaReadinessCheck<EmbeddingCallCheck>();
services.AddMinervaReadinessCheck<LlmCallCheck>();
```

### Step 4: Unit + integration tests

**Unit tests** in `tests/Minerva.UnitTests/Readiness/Checks/`:
- One file per check (`ConnectionStringParseCheckTests`, `PostgresConnectivityCheckTests`, `PgVectorExtensionCheckTests`, `EmbeddingCallCheckTests`, `LlmCallCheckTests`).
- Each covers: short-circuit branch (feature off) + every `Code` branch with a fake/stub.
- `LlmCallCheckTests` includes the **400-passes** branch using a fake `IChatClientFacade` returning a `ClientResultException` with `Status = 400`.
- `PostgresConnectivityCheckTests` verifies SQLSTATE-to-Code mapping with a fake `NpgsqlException` (or a thin DB-call seam).
- `PgVectorExtensionCheckTests` covers all three branches (enabled / available-not-enabled / not-available).
- All failure cases assert that the `Message` is **redacted** (no `Password=...`, no `Bearer ...`).

**Integration tests** in `tests/Minerva.IntegrationTests/Readiness/`:
- `PostgresConnectivityCheckIntegrationTests` against `StorageTestFixture` — pass branch; fail branch by pointing the check at a wrong port.
- `PgVectorExtensionCheckIntegrationTests` against `StorageTestFixture` — pass branch (the fixture's `SchemaInitializer` enables `vector`).
- Reuse the `MINERVA_TEST_CONNSTRING` env-var pattern; do not introduce Testcontainers.

**Architecture-test update**: `tests/Minerva.ArchitectureTests/LayerDependencyTests.cs` — `Minerva.Readiness.Checks` namespace touches `Npgsql` and the OpenAI SDK; partition it into the **adapter ring**, distinct from `Minerva.Readiness` (use-case ring) added in PRD 01.

---

## Files Changed

### New Files

| File | Purpose |
| ------ | --------- |
| `src/Minerva/Readiness/Checks/ConnectionStringParseCheck.cs` | Pure config check |
| `src/Minerva/Readiness/Checks/PostgresConnectivityCheck.cs` | Storage check |
| `src/Minerva/Readiness/Checks/PgVectorExtensionCheck.cs` | Storage check |
| `src/Minerva/Readiness/Checks/EmbeddingCallCheck.cs` | Embedding check |
| `src/Minerva/Readiness/Checks/LlmCallCheck.cs` | LLM check |
| `tests/Minerva.UnitTests/Readiness/Checks/ConnectionStringParseCheckTests.cs` | Unit tests |
| `tests/Minerva.UnitTests/Readiness/Checks/PostgresConnectivityCheckTests.cs` | Unit tests |
| `tests/Minerva.UnitTests/Readiness/Checks/PgVectorExtensionCheckTests.cs` | Unit tests |
| `tests/Minerva.UnitTests/Readiness/Checks/EmbeddingCallCheckTests.cs` | Unit tests |
| `tests/Minerva.UnitTests/Readiness/Checks/LlmCallCheckTests.cs` | Unit tests (incl. 400-passes branch) |
| `tests/Minerva.IntegrationTests/Readiness/PostgresConnectivityCheckIntegrationTests.cs` | Live DB |
| `tests/Minerva.IntegrationTests/Readiness/PgVectorExtensionCheckIntegrationTests.cs` | Live DB |

### Modified Files

| File | Changes |
| ------ | --------- |
| `src/Minerva/Llm/OpenAICompatibleLlmProvider.cs` | Add `CheckAvailabilityAsync()` bypassing Polly + RateLimiter; HTTP 400 → returns successfully; introduce internal `IChatClientFacade` test seam |
| `src/Minerva/DI/ServiceCollectionExtensions.cs` | Call `AddMinervaReadinessCore()`; register the five library checks unconditionally |
| `tests/Minerva.ArchitectureTests/LayerDependencyTests.cs` | Place `Minerva.Readiness.Checks` in adapter ring (distinct from `Minerva.Readiness` use-case ring) |

---

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] `dotnet test tests/Minerva.UnitTests` is green
- [ ] `MINERVA_TEST_CONNSTRING=… dotnet test tests/Minerva.IntegrationTests` is green
- [ ] `dotnet test tests/Minerva.ArchitectureTests` is green
- [ ] Manual smoke: with `Minerva.MarkdownWatcher` configured against a healthy local Postgres + reachable embedder + LLM, a one-off resolution of `IReadinessChecker.CheckReadinessAsync()` returns `IsReady = true`
- [ ] Manual smoke: with Postgres pointed at a non-existent database (e.g. `Database=does_not_exist`), the report contains a single failed check with `Code = "MINERVA.POSTGRES.DATABASE_MISSING"` and the literal `CREATE DATABASE` remediation
- [ ] All failure-case `Message` fields are redacted (passwords/bearer tokens absent)
- [ ] `LlmCallCheck` 400-passes branch is covered by a unit test that uses the `IChatClientFacade` seam

⏸️ **GATE**: Sub-PRD complete. Continue to next sub-PRD or `/dev-checkpoint`.
