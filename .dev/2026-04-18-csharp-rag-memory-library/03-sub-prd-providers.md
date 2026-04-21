# Sub-PRD: Provider Layer

**Parent**: [00-master-plan.md](./00-master-plan.md)
**Status**: Complete
**Dependency**: [01-sub-prd-scaffold.md](./01-sub-prd-scaffold.md)
**Last Updated**: 2026-04-07

---

## Implementation Progress

| Step | Description | Status |
|------|-------------|--------|
| **1** | Create RateLimiter | ✅ Complete |
| **2** | Create OpenAICompatibleEmbeddingProvider | ✅ Complete |
| **3** | Create OpenAICompatibleLlmProvider | ✅ Complete |
| **4** | Create ProviderFactory | ✅ Complete |
| **5** | Write unit tests | ✅ Complete |

---

## Goal

Implement rate-limited, retry-backed embedding and LLM providers that wrap the OpenAI SDK and expose `Microsoft.Extensions.AI` interfaces (`IEmbeddingGenerator<string, Embedding<float>>` and `IChatClient`). These providers work with any OpenAI-compatible endpoint (OpenAI, Ollama, LM Studio, vLLM, etc.).

---

## Implementation Steps

### Step 1: Create RateLimiter

**File**: `src/Minerva/Providers/RateLimiter.cs`

Port of v1's Python `RateLimiter` using C# async primitives:

- **Concurrency control**: `SemaphoreSlim` — limits parallel in-flight requests (e.g., 1 for Ollama, 10 for OpenAI)
- **RPM throttle**: Sliding-window token bucket — tracks request timestamps in a `ConcurrentQueue<DateTimeOffset>`, blocks when window is full
- Configurable via `ProviderOptions.Concurrency` and `ProviderOptions.RequestsPerMinute`
- When `RequestsPerMinute` is null, RPM throttle is disabled (local models often have no RPM limit)
- `AcquireAsync(CancellationToken)` — acquires both semaphore slot and RPM token
- `Release()` — releases semaphore slot

```csharp
public class RateLimiter : IDisposable
{
    public RateLimiter(int concurrency, int? requestsPerMinute) { ... }
    public Task AcquireAsync(CancellationToken ct = default) { ... }
    public void Release() { ... }
}
```

### Step 2: Create OpenAICompatibleEmbeddingProvider

**File**: `src/Minerva/Providers/OpenAICompatibleEmbeddingProvider.cs`

Implements `IEmbeddingGenerator<string, Embedding<float>>` from `Microsoft.Extensions.AI`.

Key behaviors:
- Wraps `OpenAI.OpenAIClient` configured with custom `BaseUrl` from `ProviderOptions`
- Applies `RateLimiter` around every API call
- Uses Polly v8 resilience pipeline: exponential backoff (factor 2, jitter) on HTTP 429 and 5xx, max 3 retries
- **L2 normalization**: After receiving embeddings, normalize each vector to unit length. Formula: `v[i] / sqrt(sum(v[j]^2))`. This ensures `dot product == cosine similarity`.
- Validates embedding dimension matches expected value (from collection metadata)

```csharp
public class OpenAICompatibleEmbeddingProvider : IEmbeddingGenerator<string, Embedding<float>>
{
    public OpenAICompatibleEmbeddingProvider(
        ProviderOptions options, RateLimiter rateLimiter) { ... }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken ct = default) { ... }
}
```

### Step 3: Create OpenAICompatibleLlmProvider

**File**: `src/Minerva/Providers/OpenAICompatibleLlmProvider.cs`

Implements `IChatClient` from `Microsoft.Extensions.AI`.

Key behaviors:
- Wraps `OpenAI.OpenAIClient` with custom `BaseUrl`
- Applies `RateLimiter` around every API call
- Uses Polly v8 resilience pipeline (same as embedding provider)
- Used for document summarization and chunk contextualization (both optional)
- Supports streaming via `IChatClient.GetStreamingResponseAsync`

### Step 4: Create ProviderFactory

**File**: `src/Minerva/Providers/ProviderFactory.cs`

```csharp
public class ProviderFactory
{
    public ProviderFactory(CredentialResolver credentialResolver) { ... }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingProvider(
        ProviderOptions options) { ... }

    public IChatClient? CreateLlmProvider(
        ProviderOptions? options) { ... }
}
```

- Resolves API key via `CredentialResolver` (handles `${ENV_VAR}` syntax)
- Creates `RateLimiter` from options
- Constructs `OpenAIClient` with resolved base URL and API key
- Wraps in the appropriate provider class
- Returns `null` for LLM if options are null (summarization/contextualization disabled)

### Step 5: Write unit tests

**File**: `tests/Minerva.Tests/Providers/RateLimiterTests.cs`
- Concurrency limit respected: N+1 concurrent calls, last one waits
- RPM limit respected: burst of requests, verify delay kicks in
- Null RPM disables throttle
- CancellationToken cancels blocked acquire

**File**: `tests/Minerva.Tests/Providers/EmbeddingProviderTests.cs`
- L2 normalization: known input vector, verify output has unit length (norm ≈ 1.0 ± 0.001)
- Dimension validation: mismatched dimension throws `EmbeddingException`
- Retry on 429: mock HTTP handler returns 429 then 200, verify success after retry
- Retry exhausted: mock HTTP handler returns 429 three times, verify `EmbeddingException`

**File**: `tests/Minerva.Tests/Providers/ProviderFactoryTests.cs`
- Creates embedding provider from valid options
- Creates LLM provider from valid options
- Returns null LLM when options are null
- Rejects literal API key (not `${ENV_VAR}` pattern) — delegates to `CredentialResolver`

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `src/Minerva/Providers/RateLimiter.cs` | Sliding-window token bucket with concurrency control |
| `src/Minerva/Providers/OpenAICompatibleEmbeddingProvider.cs` | IEmbeddingGenerator with L2 norm + Polly retry |
| `src/Minerva/Providers/OpenAICompatibleLlmProvider.cs` | IChatClient with rate limit + Polly retry |
| `src/Minerva/Providers/ProviderFactory.cs` | Constructs providers from MinervaOptions |
| `tests/Minerva.Tests/Providers/RateLimiterTests.cs` | Rate limiter unit tests |
| `tests/Minerva.Tests/Providers/EmbeddingProviderTests.cs` | Embedding provider unit tests |
| `tests/Minerva.Tests/Providers/ProviderFactoryTests.cs` | Factory unit tests |

---

## Verification Checklist

- [ ] RateLimiter respects concurrency and RPM limits
- [ ] Embedding provider normalizes vectors to unit length
- [ ] Embedding provider retries on 429/5xx with exponential backoff
- [ ] ProviderFactory resolves `${ENV_VAR}` credentials
- [ ] ProviderFactory rejects literal API keys
- [ ] Run: `dotnet test tests/Minerva.Tests --filter Category=Providers`

⏸️ **GATE**: Sub-PRD complete. Continue to next sub-PRD or `/dev-checkpoint`.
