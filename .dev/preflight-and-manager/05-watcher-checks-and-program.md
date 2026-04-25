# Sub-PRD: Watcher Checks and Program.cs

**Parent**: [00-master-plan.md](./00-master-plan.md)
**Status**: Not Started
**Dependency**: [04-builtin-library-checks.md](./04-builtin-library-checks.md)
**Last Updated**: 2026-04-25

---

## Implementation Progress

| Step | Description | Status |
|------|-------------|--------|
| **1** | Three watcher checks (`RootPathExists`, `CollectionNameValid`, `CollectionDimensionMatch`) | ⬜ Not Started |
| **2** | Register from `AddMinervaWatcher()` | ⬜ Not Started |
| **3** | Rewrite `Program.cs` as `async Task<int>` with pre-host preflight snippet | ⬜ Not Started |
| **4** | Unit + integration tests; manual end-to-end exit-code verification | ⬜ Not Started |

---

## Goal

Implement the three watcher-specific readiness checks, register them from `AddMinervaWatcher()`, and rewrite `Minerva.MarkdownWatcher/Program.cs` as `async Task<int>` with the explicit pre-host snippet from the brief. After this PRD, the watcher exits with code `2` and a structured report when any prerequisite fails, and starts cleanly when everything is healthy.

---

## Implementation Steps

### Step 1: Three watcher checks

All under `src/Minerva.MarkdownWatcher/Readiness/`. Each implements `IReadinessCheck` and `IReadinessCheckTimeout` (interfaces from PRD 01).

#### `RootPathExistsCheck`

- **File**: `src/Minerva.MarkdownWatcher/Readiness/RootPathExistsCheck.cs`
- **Category**: `Client`. **Timeout**: 2s.
- Reads `IOptions<WatcherOptions>`.
- If `RootPath` null/whitespace → fail with `Code = "MINERVA.CLIENT.ROOT_PATH_MISSING"`, remediation `"Set Watcher:RootPath to the directory you want to index."`.
- If `Directory.Exists(RootPath)` is false → fail with `Code = "MINERVA.CLIENT.ROOT_PATH_MISSING"`, remediation `"Watched directory '<path>' does not exist. Create it (mkdir -p '<path>') or update Watcher:RootPath."`.
- If exists but unreadable (`UnauthorizedAccessException` on listing) → fail with `Code = "MINERVA.CLIENT.ROOT_PATH_UNREADABLE"`, remediation `"Process lacks read permission on '<path>'. Adjust filesystem permissions or run the watcher as a user with access."`.
- Else pass.

#### `CollectionNameValidCheck`

- **File**: `src/Minerva.MarkdownWatcher/Readiness/CollectionNameValidCheck.cs`
- **Category**: `Client`. **Timeout**: 2s.
- Reads `IOptions<WatcherOptions>`.
- Validates `CollectionName` against `^[a-zA-Z0-9][a-zA-Z0-9-]*$`.
- This is the **third copy** of the regex (alongside `CollectionManager.cs:84` and `SchemaInitializer.cs:106`). **Per design, the regex is duplicated, not centralised** — the `SchemaInitializer` copy is a deliberate DDL-injection guard. Do not refactor.
- On mismatch: fail with `Code = "MINERVA.CLIENT.COLLECTION_NAME_INVALID"`, remediation `"Collection name must match ^[a-zA-Z0-9][a-zA-Z0-9-]*$ (start with a letter or digit; only letters, digits, hyphens)."`.

#### `CollectionDimensionMatchCheck`

- **File**: `src/Minerva.MarkdownWatcher/Readiness/CollectionDimensionMatchCheck.cs`
- **Category**: `Client`. **Timeout**: 5s.
- Reads `IOptions<MinervaOptions>`, `IOptions<WatcherOptions>`, optional `NpgsqlDataSource`, optional `IEmbeddingDimensionProvider` (both may be null when storage/embedding are unconfigured — short-circuit to passing in that case).
- Branch order **must** be:
  1. **Embedder unavailable**: `try { dim = await provider.GetDimensionAsync(ct); } catch { return Pass(message: "skipped: embedder unavailable"); }`. The failure was already reported upstream by `EmbeddingCallCheck`; repeating it adds noise.
  2. **Collections table missing**: query `SELECT embedding_dimension FROM collections WHERE name = @name`. On `PostgresException` with SQLSTATE `42P01` → pass trivially (fresh DB, schema not yet initialised).
  3. **No row matching configured name**: query returns 0 rows → pass trivially (the collection will be created on first run).
  4. **Comparison**: stored dim != probed dim → fail with `Code = "MINERVA.CLIENT.DIMENSION_MISMATCH"`, remediation:
     ```
     Configured embedder produces N dimensions; existing collection '<name>' uses M.
     Either revert the embedder change, or drop the collection and its data
     (DELETE FROM collections WHERE name='<name>') and let it rebuild on next run.
     ```
- The dimension comes from the cache populated by `EmbeddingCallCheck` — no second network call.

### Step 2: Register from `AddMinervaWatcher()`

**File**: `src/Minerva.MarkdownWatcher/DI/ServiceCollectionExtensions.cs`

Inside `AddMinervaWatcher`:

```csharp
services.AddMinervaReadinessCheck<RootPathExistsCheck>();
services.AddMinervaReadinessCheck<CollectionNameValidCheck>();
services.AddMinervaReadinessCheck<CollectionDimensionMatchCheck>();
```

Registration is unconditional per Decision 5; checks short-circuit on null dependencies.

### Step 3: Rewrite `Program.cs`

**File**: `src/Minerva.MarkdownWatcher/Program.cs`

Change to `async Task<int>` top-level program:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minerva.DI;
using Minerva.Readiness;
using Minerva.MarkdownWatcher.DI;

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddMinerva(/* existing config binding */);
    builder.Services.AddMinervaWatcher(/* existing config binding */);

    var host = builder.Build();

    using (var scope = host.Services.CreateScope())
    {
        var checker = scope.ServiceProvider.GetRequiredService<IReadinessChecker>();
        var logger  = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var report  = await checker.CheckReadinessAsync();

        ReadinessReportFormatter.LogReport(logger, report);

        if (!report.IsReady)
        {
            return 2;
        }
    }

    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    // Bootstrap-time failure (DI build error, config binding failure, etc.).
    // The readiness pipeline catches its own exceptions, so this is for genuinely
    // unexpected failures only.
    Console.Error.WriteLine($"Unhandled exception during startup: {ex}");
    return 1;
}
```

Exit codes: **0** = ready and host completed normally, **1** = unhandled exception in `Main`, **2** = preflight failed.

If the existing `Program.cs` does additional configuration (env files, custom logging), preserve it inside the `try`.

### Step 4: Unit + integration tests

**Unit tests** in `tests/Minerva.MarkdownWatcher.UnitTests/Readiness/` (or wherever the watcher's unit-test project lives):

- **`RootPathExistsCheckTests`**: present dir → pass; missing dir → fail with `ROOT_PATH_MISSING`; null/whitespace `RootPath` → fail with `ROOT_PATH_MISSING`; unreadable → fail with `ROOT_PATH_UNREADABLE` (use a temp dir + chmod where feasible on darwin, or a stubbed seam).
- **`CollectionNameValidCheckTests`**: pass cases (`a`, `Z`, `0`, `a-b-c`, `Abc-123`); fail cases (`-leading`, `with space`, `with_underscore`, `Abc!`, empty, null). Mirror existing `CollectionManagerTests` cases.
- **`CollectionDimensionMatchCheckTests`**: each of the four Decision-10 branches. Use a fake `IEmbeddingDimensionProvider` and a thin DB-call seam. Assert remediation text contains the literal `DELETE FROM collections WHERE name='<name>'` substring.

**Integration test** in `tests/Minerva.IntegrationTests/Readiness/`:

- **`CollectionDimensionMatchCheckIntegrationTests`** against `StorageTestFixture`:
  - Branch 2 (table missing): drop the `collections` table inside the test, run the check → pass.
  - Branch 3 (no row): use a unique collection name → pass.
  - Branch 4 (mismatch): seed a row with `embedding_dimension = 1024`; fake provider returns `768`; check fails with `MINERVA.CLIENT.DIMENSION_MISMATCH` and remediation contains the expected literal.

**Manual end-to-end smoke**:
- Healthy: `dotnet run --project src/Minerva.MarkdownWatcher` → host starts; existing watcher integration unchanged; on graceful shutdown `$? == 0`.
- Misconfigured: point the connection string at `localhost:1` (or stop Postgres). Run the watcher. Expect `$? == 2`; logs contain a `LogError` per failed check with `Code` / `Message` / `Remediation`.
- Probe-skipped warning: temporarily comment out the preflight scope in `Program.cs` and run with full config. The `MinervaStartupService` warning fires once.

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `src/Minerva.MarkdownWatcher/Readiness/RootPathExistsCheck.cs` | Watcher check |
| `src/Minerva.MarkdownWatcher/Readiness/CollectionNameValidCheck.cs` | Watcher check |
| `src/Minerva.MarkdownWatcher/Readiness/CollectionDimensionMatchCheck.cs` | Watcher check (Decision-10 branches) |
| `tests/Minerva.MarkdownWatcher.UnitTests/Readiness/RootPathExistsCheckTests.cs` | Unit tests |
| `tests/Minerva.MarkdownWatcher.UnitTests/Readiness/CollectionNameValidCheckTests.cs` | Unit tests |
| `tests/Minerva.MarkdownWatcher.UnitTests/Readiness/CollectionDimensionMatchCheckTests.cs` | Unit tests |
| `tests/Minerva.IntegrationTests/Readiness/CollectionDimensionMatchCheckIntegrationTests.cs` | DB-backed integration tests |

### Modified Files

| File | Changes |
|------|---------|
| `src/Minerva.MarkdownWatcher/DI/ServiceCollectionExtensions.cs` | Register the three watcher checks |
| `src/Minerva.MarkdownWatcher/Program.cs` | `async Task<int> Main`; explicit pre-host preflight snippet; exit 0/1/2 |

---

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] `dotnet test tests/Minerva.MarkdownWatcher.UnitTests` (or equivalent) is green — all four `CollectionDimensionMatchCheck` branches covered
- [ ] `MINERVA_TEST_CONNSTRING=… dotnet test tests/Minerva.IntegrationTests --filter Readiness` is green
- [ ] `dotnet test tests/Minerva.ArchitectureTests` is green
- [ ] **End-to-end healthy**: `dotnet run --project src/Minerva.MarkdownWatcher` against a healthy local Postgres + reachable embedder + LLM starts and runs; graceful Ctrl-C exits 0
- [ ] **End-to-end misconfigured**: with Postgres unreachable, the same command exits with `$? == 2`; logs contain a `LogError` per failed check with `Code` / `Message` / `Remediation`; **no secrets leak into any logged `Message`**
- [ ] **Probe-skipped warning**: skipping the preflight in `Program.cs` and starting the host emits the safety-net `LogWarning` from `MinervaStartupService` once at `Warning` level
- [ ] `CollectionDimensionMatchCheck` mismatch remediation contains the literal `DELETE FROM collections WHERE name=` substring (asserted by integration test)

⏸️ **GATE**: Feature complete. `/dev-checkpoint` or merge.
