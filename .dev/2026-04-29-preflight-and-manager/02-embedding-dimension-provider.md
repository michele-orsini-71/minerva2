# Sub-PRD: Embedding Dimension Provider

**Parent**: [00-master-plan.md](./00-master-plan.md)
**Status**: Not Started
**Dependency**: [01-readiness-core.md](./01-readiness-core.md)
**Last Updated**: 2026-04-25

---

## Implementation Progress

| Step | Description | Status |
|------|-------------|--------|
| **1** | `IEmbeddingDimensionProvider` interface + provider implementation | ⬜ Not Started |
| **2** | Switch runtime caller; delete dead injection | ⬜ Not Started |
| **3** | Unit tests (cancellation-poisoning, failure-caching, success-caching) | ⬜ Not Started |

---

## Goal

Lift embedding-dimension discovery into a memoized provider accessor (`IEmbeddingDimensionProvider`) implemented on `OpenAICompatibleEmbeddingProvider`. Use the cancellation-safe `Lazy<Task<int>>` shape and bypass Polly + the `RateLimiter`. Delete the duplicate runtime probe in `MarkdownSyncService` and the dead `IEmbeddingGenerator<string, Embedding<float>>` injection (which is not registered in DI today — a live bug). After this PRD, every consumer that needs the dimension calls a single method.

---

## Implementation Steps

### Step 1: Interface + provider implementation

**Files**:
- `src/Minerva/Embedding/IEmbeddingDimensionProvider.cs` (new)
- `src/Minerva/Embedding/OpenAICompatibleEmbeddingProvider.cs` (modify)
- `src/Minerva/DI/ServiceCollectionExtensions.cs` (modify — register the same instance under the new interface)

Interface:

```csharp
public interface IEmbeddingDimensionProvider
{
    Task<int> GetDimensionAsync(CancellationToken ct = default);
}
```

`OpenAICompatibleEmbeddingProvider` modifications:
- Implement `IEmbeddingDimensionProvider`.
- Add private field `Lazy<Task<int>> _dimensionLazy`, initialised in the constructor:

```csharp
_dimensionLazy = new Lazy<Task<int>>(
    () => ProbeDimensionCoreAsync(CancellationToken.None),
    LazyThreadSafetyMode.ExecutionAndPublication);
```

The factory uses **`CancellationToken.None`** so no caller's CT can poison the cache.

- `GetDimensionAsync(CancellationToken ct)` returns `_dimensionLazy.Value.WaitAsync(ct)` — caller cancellation is composed at the await, not at the underlying task.

- `ProbeDimensionCoreAsync` calls the OpenAI SDK `_client.GenerateEmbeddingsAsync` (or the SDK 2.10.0 equivalent) **directly** — bypassing both `_resiliencePipeline` and `_rateLimiter`. Input is the literal string `"preflight"`. Returns the vector length.

- The factory body must be **async** (returning a `Task`) so synchronous throws inside the SDK setup do not poison `Lazy<Task<int>>` permanently. If a synchronous step is needed, wrap the body in `async () => { … }`.

- Translate SDK exceptions to `ProviderUnavailableException` so consumers (`EmbeddingCallCheck`, `CollectionDimensionMatchCheck`) can catch a single Minerva-typed exception. Failure-caching is by design — see Decision 12 in the brief.

DI registration: in the existing factory that constructs `OpenAICompatibleEmbeddingProvider`, also register the same instance under `IEmbeddingDimensionProvider`. Ensure provider lifetime is **singleton** so the lazy field caches across the process.

### Step 2: Switch runtime caller; delete dead injection

**File**: `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs`

- Delete `private async Task<int> ProbeEmbeddingDimensionAsync(CancellationToken ct)` (around line 101) and any helper code for it.
- Remove the constructor parameter `IEmbeddingGenerator<string, Embedding<float>>` (this type is **not registered** in DI — a latent failure mode resolved by deletion).
- Add constructor parameter `IEmbeddingDimensionProvider _dimensionProvider`.
- Replace the call site (around line 88) of the deleted probe with `await _dimensionProvider.GetDimensionAsync(ct)`.

After this step, `MarkdownSyncService` no longer references `Microsoft.Extensions.AI.IEmbeddingGenerator<,>`; remove the now-unused `using` if present.

### Step 3: Unit tests

**Files**:
- `tests/Minerva.UnitTests/Embedding/EmbeddingDimensionProviderTests.cs` (new)
- Update existing `MarkdownSyncService` tests (if present) to inject `IEmbeddingDimensionProvider` instead of the removed `IEmbeddingGenerator`.

Test seam: `OpenAICompatibleEmbeddingProvider` constructs an `EmbeddingClient` directly. To unit-test, extract a thin **internal** seam — recommended:

```csharp
internal interface IEmbeddingProbeFacade
{
    Task<int> EmbedAndCountDimensionsAsync(string input, CancellationToken ct);
}
```

The default impl wraps `_client.GenerateEmbeddingsAsync(...)`; tests inject a fake. The provider's `ProbeDimensionCoreAsync` delegates to the facade.

Test cases:
- **Cancellation-poisoning** (top risk per Decision 12): caller A awaits with a 1ms CT that fires; caller B awaits with a healthy CT. Caller A throws `OperationCanceledException`; caller B observes the dimension. Verify the underlying probe was called exactly **once**.
- **Failure-caching**: facade throws on first call. First `GetDimensionAsync()` rethrows; second `GetDimensionAsync()` rethrows the same exception type **without a new facade call** (verify call count = 1).
- **Success-caching**: 50 concurrent `GetDimensionAsync()` calls observe the value from a single underlying call (verify call count = 1).
- **Polly bypass**: facade throws on first call; verify no retries (call count = 1, not 4).
- **Lifetime alignment**: a test that resolves `IEmbeddingDimensionProvider` and `IEmbeddingClient` from the same `IServiceProvider` and asserts they are the same instance.

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `src/Minerva/Embedding/IEmbeddingDimensionProvider.cs` | Single-method interface |
| `tests/Minerva.UnitTests/Embedding/EmbeddingDimensionProviderTests.cs` | Concurrency / cancellation / failure-caching tests |

### Modified Files

| File | Changes |
|------|---------|
| `src/Minerva/Embedding/OpenAICompatibleEmbeddingProvider.cs` | Implement `IEmbeddingDimensionProvider`; `Lazy<Task<int>>` field with `CT.None` factory; private `ProbeDimensionCoreAsync` calling `_client` directly (bypass Polly + RateLimiter); translate SDK exceptions to `ProviderUnavailableException` |
| `src/Minerva/DI/ServiceCollectionExtensions.cs` | Register the same provider instance under `IEmbeddingDimensionProvider` (forwarding registration) |
| `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs` | Delete `ProbeEmbeddingDimensionAsync`; remove `IEmbeddingGenerator` ctor parameter; inject `IEmbeddingDimensionProvider`; switch caller to `GetDimensionAsync(ct)` |

### Deleted

| Element | Reason |
|---------|--------|
| `MarkdownSyncService.ProbeEmbeddingDimensionAsync` (method body) | Replaced by `IEmbeddingDimensionProvider.GetDimensionAsync` |
| `MarkdownSyncService` constructor parameter `IEmbeddingGenerator<string, Embedding<float>>` | Was never registered in DI (latent bug); no longer needed |

---

## Verification Checklist

- [ ] `dotnet build` succeeds (the unbound `IEmbeddingGenerator` removal removes a latent failure mode)
- [ ] No remaining solution-wide reference to `ProbeEmbeddingDimensionAsync` (`grep -r ProbeEmbeddingDimensionAsync src/ tests/` returns nothing)
- [ ] `dotnet test tests/Minerva.UnitTests` is green
- [ ] Cancellation-poisoning test asserts the underlying probe ran exactly once even when the first caller's CT fires
- [ ] `IEmbeddingClient` and `IEmbeddingDimensionProvider` resolve to the same singleton instance
- [ ] `dotnet test tests/Minerva.ArchitectureTests` is green

⏸️ **GATE**: Sub-PRD complete. Continue to next sub-PRD or `/dev-checkpoint`.
