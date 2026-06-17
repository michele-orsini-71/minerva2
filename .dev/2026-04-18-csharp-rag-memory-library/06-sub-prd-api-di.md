# Sub-PRD: Public API & DI

**Parent**: [00-master-plan.md](./00-master-plan.md)
**Status**: Complete
**Dependency**: [04-sub-prd-ingestion.md](./04-sub-prd-ingestion.md), [05-sub-prd-search.md](./05-sub-prd-search.md)
**Last Updated**: 2026-04-07

---

## Implementation Progress

| Step | Description | Status |
| ------ | ------------- | -------- |
| **1** | Create CollectionManager | ✅ Complete |
| **2** | Create MinervaEngine | ✅ Complete |
| **3** | Create DI extensions | ✅ Complete |
| **4** | Create MinervaStartupService | ✅ Complete |
| **5** | Write end-to-end integration test | ✅ Complete |

---

## Goal

Expose the unified `MinervaEngine` public facade with `IngestAsync`, `RemoveAsync`, `SearchAsync`. Wire all internal services via `Microsoft.Extensions.DependencyInjection`. Provide a hosted service for schema initialization on startup. Validate with an end-to-end integration test against a real PostgreSQL instance.

---

## Implementation Steps

### Step 1: Create CollectionManager

**File**: `src/Minerva/Collections/CollectionManager.cs`

High-level collection CRUD that wraps `ICollectionRepository` with validation:

```csharp
public class CollectionManager
{
    public CollectionManager(
        ICollectionRepository collectionRepository,
        SchemaInitializer schemaInitializer) { ... }

    public Task<Collection> CreateAsync(
        string name, string embeddingModel, int embeddingDimension,
        string? description = null, Dictionary<string, object>? metadata = null,
        CancellationToken ct = default) { ... }

    public Task<Collection?> GetAsync(string name, CancellationToken ct = default) { ... }
    public Task<IReadOnlyList<Collection>> ListAsync(CancellationToken ct = default) { ... }
    public Task DeleteAsync(string name, CancellationToken ct = default) { ... }
}
```

On `CreateAsync`:
- Validate name is non-empty, alphanumeric + hyphens
- Validate no actual API keys in metadata (delegate to `CredentialResolver` guard)
- Create the collection record
- Call `SchemaInitializer.EnsureHnswIndexAsync(name, dimension)` to create the per-collection HNSW index

### Step 2: Create MinervaEngine

**File**: `src/Minerva/MinervaEngine.cs`

The public API facade — the only type most consumers need to interact with:

```csharp
public class MinervaEngine
{
    public MinervaEngine(
        IngestionPipeline ingestionPipeline,
        SearchPipeline searchPipeline,
        CollectionManager collectionManager,
        ILogger<MinervaEngine> logger) { ... }

    // Ingest or update a document — idempotent by sourceId
    public Task<IngestionResult> IngestAsync(
        string collectionName,
        Document document,
        CancellationToken ct = default) { ... }

    // Remove a document and all its chunks
    public Task RemoveAsync(
        string collectionName,
        string sourceId,
        CancellationToken ct = default) { ... }

    // Search across one or more collections
    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        IReadOnlyList<string> collectionNames,
        SearchOptions? options = null,
        CancellationToken ct = default) { ... }

    // Collection management
    public CollectionManager Collections { get; }
}
```

`IngestAsync`:
- Validates collection exists (throws `ConfigurationException` if not)
- Validates embedding model matches collection's model
- Delegates to `IngestionPipeline.IngestAsync`

`SearchAsync`:
- Validates all collections exist
- Defaults `options` to `new SearchOptions()` if null
- Delegates to `SearchPipeline.SearchAsync`

### Step 3: Create DI extensions

**File**: `src/Minerva/DI/ServiceCollectionExtensions.cs`

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMinerva(
        this IServiceCollection services,
        Action<MinervaOptions> configure)
    {
        services.Configure(configure);

        // Configuration
        services.AddSingleton<CredentialResolver>();

        // Storage
        services.AddSingleton<SchemaInitializer>();
        services.AddSingleton<ICollectionRepository, PostgresCollectionRepository>();
        services.AddSingleton<IChunkRepository, PostgresChunkRepository>();

        // Providers (constructed via factory)
        services.AddSingleton<ProviderFactory>();
        services.AddSingleton(sp => {
            var factory = sp.GetRequiredService<ProviderFactory>();
            var options = sp.GetRequiredService<IOptions<MinervaOptions>>().Value;
            return factory.CreateEmbeddingProvider(options.Embedding);
        });
        services.AddSingleton(sp => {
            var factory = sp.GetRequiredService<ProviderFactory>();
            var options = sp.GetRequiredService<IOptions<MinervaOptions>>().Value;
            return factory.CreateLlmProvider(options.Llm);
        });

        // NpgsqlDataSource
        services.AddSingleton(sp => {
            var options = sp.GetRequiredService<IOptions<MinervaOptions>>().Value;
            var dsBuilder = new NpgsqlDataSourceBuilder(options.ConnectionString);
            dsBuilder.UseVector();
            return dsBuilder.Build();
        });

        // Ingestion
        services.AddSingleton<DocumentChunker>();
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<DocumentSummarizer>();  // no-ops when disabled
        services.AddSingleton<ChunkContextualizer>(); // no-ops when disabled
        services.AddSingleton<IngestionPipeline>();

        // Search
        services.AddSingleton<VectorSearch>();
        services.AddSingleton<FullTextSearch>();
        services.AddSingleton<ContextExpander>();
        services.AddSingleton<SearchPipeline>();

        // Collections
        services.AddSingleton<CollectionManager>();

        // Public API
        services.AddSingleton<MinervaEngine>();

        // Startup
        services.AddHostedService<MinervaStartupService>();

        return services;
    }
}
```

Note: Lifetimes may need adjustment during implementation (e.g., scoped for NpgsqlConnection). The above is the starting point.

### Step 4: Create MinervaStartupService

**File**: `src/Minerva/DI/MinervaStartupService.cs`

```csharp
public class MinervaStartupService : IHostedService
{
    public MinervaStartupService(SchemaInitializer schemaInitializer) { ... }

    public Task StartAsync(CancellationToken ct)
    {
        return _schemaInitializer.InitializeAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

Runs schema migrations on application startup. Idempotent — safe to run every time.

### Step 5: Write end-to-end integration test

**File**: `tests/Minerva.IntegrationTests/EndToEnd/MinervaEngineE2ETests.cs`

Requires a running PostgreSQL instance with pgvector extension.

Test flow:
1. Build a `ServiceProvider` with `AddMinerva()` configured for the test DB
2. Resolve `MinervaEngine`
3. Create a collection via `engine.Collections.CreateAsync`
4. Ingest two markdown documents with `IngestAsync`
5. Verify `IngestionResult.Added` counts are correct
6. Re-ingest same document unchanged — verify `IngestionResult.Unchanged == 1`
7. Search for a term present in one document — verify it's in results
8. Remove one document with `RemoveAsync`
9. Search again — verify removed document no longer appears
10. Delete the collection

```csharp
[Collection("E2E")]
[Trait("Category", "E2E")]
public class MinervaEngineE2ETests : IAsyncLifetime
{
    // Use a mock/local embedding provider for E2E tests
    // to avoid requiring a real model server
}
```

Note: E2E tests should use a mock embedding provider (returns random vectors of correct dimension) to avoid depending on a running model server. The mock must still go through the full pipeline.

---

## Files Changed

### New Files

| File | Purpose |
| ------ | --------- |
| `src/Minerva/Collections/CollectionManager.cs` | Collection CRUD with validation |
| `src/Minerva/MinervaEngine.cs` | Public facade: IngestAsync, RemoveAsync, SearchAsync |
| `src/Minerva/DI/ServiceCollectionExtensions.cs` | AddMinerva() DI extension |
| `src/Minerva/DI/MinervaStartupService.cs` | IHostedService for schema init on startup |
| `tests/Minerva.IntegrationTests/EndToEnd/MinervaEngineE2ETests.cs` | Full E2E integration test |

---

## Verification Checklist

- [ ] `services.AddMinerva(...)` resolves `MinervaEngine` without DI errors
- [ ] `MinervaStartupService` runs schema migrations on startup
- [ ] E2E: Ingest → Search → Remove → Verify cycle passes
- [ ] E2E: Re-ingest unchanged document returns `Unchanged == 1`
- [ ] Collection creation triggers HNSW index creation
- [ ] Run: `dotnet test tests/Minerva.IntegrationTests --filter Category=E2E`

⏸️ **GATE**: Sub-PRD complete. Continue to next sub-PRD or `/dev-checkpoint`.
