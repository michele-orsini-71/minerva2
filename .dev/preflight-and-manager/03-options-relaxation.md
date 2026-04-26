# Sub-PRD: Options Relaxation and Conditional DI Registration

**Parent**: [00-master-plan.md](./00-master-plan.md)
**Status**: Done
**Dependency**: [01-readiness-core.md](./01-readiness-core.md) (independent of 02; can be done in parallel)
**Last Updated**: 2026-04-25

---

## Implementation Progress

| Step | Description | Status |
|------|-------------|--------|
| **1** | Relax `MinervaOptions` (drop `required`; nullable) | ✅ Done |
| **2** | Conditional `AddMinerva()` registration; null-guard call sites | ✅ Done |
| **3** | Tests for no-config / partial-config / full-config DI | ✅ Done |

---

## Goal

Make `MinervaOptions.ConnectionString` and `MinervaOptions.Embedding` nullable (joining `Llm`, already nullable). Make `AddMinerva()` register storage and embedding services **conditionally** on those properties being non-null. Guard remaining call sites against the new nullability. After this PRD, `services.AddMinerva(_ => { })` builds a container without throwing; only configured features get wired.

---

## Implementation Steps

### Step 1: Relax `MinervaOptions`

**File**: `src/Minerva/Configuration/MinervaOptions.cs`

- Drop the `required` modifier from `ConnectionString` and `Embedding`.
- Make both nullable: `string?` and `ProviderOptions?` (or whatever the existing type for `Embedding` is).
- Update XML docs: "null means the feature is not configured; readiness checks for that feature will short-circuit to passing."

After this step, `dotnet build` will surface compile errors at every call site that treats these as non-null — those are addressed in Step 2.

### Step 2: Conditional `AddMinerva()` registration; null-guard call sites

**File**: `src/Minerva/DI/ServiceCollectionExtensions.cs`

Wrap the registration blocks in conditionals based on the `MinervaOptions` instance built from the user's `Action<MinervaOptions>`:

```csharp
public static IServiceCollection AddMinerva(this IServiceCollection services, Action<MinervaOptions> configure)
{
    var options = new MinervaOptions();
    configure(options);
    services.Configure(configure);

    if (options.ConnectionString is not null)
    {
        // Register NpgsqlDataSource, SchemaInitializer, repositories,
        // CollectionManager, MinervaStartupService, etc.
        // (existing block at line 28, plus dependent registrations)
    }

    if (options.Embedding is not null)
    {
        // Register IEmbeddingClient, OpenAICompatibleEmbeddingProvider,
        // IEmbeddingDimensionProvider (forwarding registration from PRD 02),
        // IEmbeddingService.
        // (existing block at lines 33-37 and 62-64)
    }

    if (options.Llm is not null)
    {
        // Already conditional today — confirm and tidy.
    }

    // (Readiness core registration — wired in PRD 04 when checks are added.)

    return services;
}
```

**Important**: `SchemaInitializer`, `CollectionManager`, `MinervaStartupService`, and any other type that depends on `NpgsqlDataSource` must be registered **inside** the storage block. Same for embedding-dependent types.

**Other call sites**:
- `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs:95` (`_minervaOptions.Embedding.Model`) — the watcher cannot run without an embedder, so guard with `throw new InvalidOperationException("Embedding is not configured; the watcher requires it. Run preflight before host start.")` or equivalent. In practice this never fires because PRD 05's preflight blocks startup; the throw documents the invariant.
- Audit any other site that treats `options.Embedding` or `options.ConnectionString` as non-null. **Build with nullable warnings as errors** during this PRD to catch every site mechanically.

### Step 3: Tests for no-config / partial-config / full-config DI

**File**: `tests/Minerva.UnitTests/DI/AddMinervaConditionalRegistrationTests.cs` (new)

Cases:
- **No-config**: `services.AddMinerva(_ => { }).BuildServiceProvider()` does not throw at registration or container build. Resolution checks: `sp.GetService<NpgsqlDataSource>()` is `null`; `sp.GetService<IEmbeddingClient>()` is `null`; `sp.GetService<IChatClient>()` is `null`.
- **Storage-only**: `ConnectionString` set, `Embedding` and `Llm` null → `NpgsqlDataSource` resolves; embedding/LLM do not.
- **Embedding-only**: `Embedding` set, others null → `IEmbeddingClient` and `IEmbeddingDimensionProvider` resolve; storage/LLM do not.
- **LLM-only**: `Llm` set, others null → `IChatClient` resolves; storage/embedding do not.
- **Full config**: all three resolve (matches today's behaviour).
- **Existing happy path**: existing tests in `Minerva.UnitTests` continue to pass with full config.

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `tests/Minerva.UnitTests/DI/AddMinervaConditionalRegistrationTests.cs` | Conditional registration tests |

### Modified Files

| File | Changes |
|------|---------|
| `src/Minerva/Configuration/MinervaOptions.cs` | Drop `required`; make `ConnectionString` and `Embedding` nullable |
| `src/Minerva/DI/ServiceCollectionExtensions.cs` | Wrap storage block (`:28` and dependents) and embedding block (`:33-37`, `:62-64`) in conditionals on the corresponding options being non-null |
| `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs:95` | Null-check on `_minervaOptions.Embedding`; throw `InvalidOperationException` with descriptive message if null |

---

## Verification Checklist

- [ ] `dotnet build` succeeds with **zero** new nullability warnings (run with warnings-as-errors during this PRD)
- [ ] `services.AddMinerva(_ => { }).BuildServiceProvider()` does not throw (asserted by test)
- [ ] `dotnet test tests/Minerva.UnitTests` is green
- [ ] `dotnet test tests/Minerva.IntegrationTests` (with `MINERVA_TEST_CONNSTRING`) is green — full-config behaviour unchanged
- [ ] `dotnet test tests/Minerva.ArchitectureTests` is green
- [ ] Resolving `NpgsqlDataSource` from a no-config container returns `null` (not throws)

⏸️ **GATE**: Sub-PRD complete. Continue to next sub-PRD or `/dev-checkpoint`.
