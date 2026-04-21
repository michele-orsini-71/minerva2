# Sub-PRD: Search Pipeline

**Parent**: [00-master-plan.md](./00-master-plan.md)
**Status**: Complete
**Dependency**: [02-sub-prd-storage.md](./02-sub-prd-storage.md), [03-sub-prd-providers.md](./03-sub-prd-providers.md)
**Last Updated**: 2026-04-07

---

## Implementation Progress

| Step | Description | Status |
|------|-------------|--------|
| **1** | Create VectorSearch | ✅ Complete |
| **2** | Create FullTextSearch | ✅ Complete |
| **3** | Create RankFusion | ✅ Complete |
| **4** | Create ContextExpander | ✅ Complete |
| **5** | Create SearchPipeline | ✅ Complete |
| **6** | Write unit tests | ✅ Complete |

---

## Goal

Implement hybrid search combining dense vector similarity (pgvector) and PostgreSQL full-text search (tsvector/tsquery), merged via Reciprocal Rank Fusion (RRF), with optional context expansion via adjacent chunk fetching.

---

## Implementation Steps

### Step 1: Create VectorSearch

**File**: `src/Minerva/Search/VectorSearch.cs`

Executes pgvector cosine distance query against the chunks table.

```csharp
public class VectorSearch
{
    public VectorSearch(IChunkRepository chunkRepository) { ... }

    public Task<IReadOnlyList<RankedChunk>> SearchAsync(
        string collectionName,
        float[] queryEmbedding,
        int topK,
        CancellationToken ct = default) { ... }
}
```

Returns `RankedChunk` records with the chunk data and a rank position (1-based).

### Step 2: Create FullTextSearch

**File**: `src/Minerva/Search/FullTextSearch.cs`

Executes PostgreSQL full-text search using `ts_rank` and `plainto_tsquery`.

```csharp
public class FullTextSearch
{
    public FullTextSearch(IChunkRepository chunkRepository) { ... }

    public Task<IReadOnlyList<RankedChunk>> SearchAsync(
        string collectionName,
        string query,
        int topK,
        CancellationToken ct = default) { ... }
}
```

The full-text index is built from chunk text after attachment integration but WITHOUT the contextual prefix (per PRD decision).

### Step 3: Create RankFusion

**File**: `src/Minerva/Search/RankFusion.cs`

Implements Reciprocal Rank Fusion (RRF) to merge two ranked lists into a single score.

```csharp
public static class RankFusion
{
    // k is the RRF constant (typically 60)
    // alpha controls weight: 0.0 = full-text only, 1.0 = vector only, 0.5 = equal
    public static IReadOnlyList<FusedResult> Fuse(
        IReadOnlyList<RankedChunk> vectorResults,
        IReadOnlyList<RankedChunk> ftsResults,
        double alpha = 0.5,
        int k = 60) { ... }
}
```

RRF formula per chunk:
```
score = alpha * (1 / (k + vector_rank)) + (1 - alpha) * (1 / (k + fts_rank))
```

Chunks appearing in only one list get `rank = topK + 1` for the missing list (worst possible rank).

### Step 4: Create ContextExpander

**File**: `src/Minerva/Search/ContextExpander.cs`

Fetches adjacent chunks for each search result to provide surrounding context.

```csharp
public class ContextExpander
{
    public ContextExpander(IChunkRepository chunkRepository) { ... }

    // For each result, fetches prev and next chunk content
    // Returns enriched results with ContextBefore and ContextAfter populated
    public Task<IReadOnlyList<SearchResult>> ExpandAsync(
        IReadOnlyList<SearchResult> results,
        CancellationToken ct = default) { ... }
}
```

Uses `IChunkRepository.GetAdjacentChunksAsync` with a batch of all prev/next IDs from all results in a single query (avoids N+1).

### Step 5: Create SearchPipeline

**File**: `src/Minerva/Search/SearchPipeline.cs`

Orchestrates the full search flow:

```csharp
public class SearchPipeline
{
    public SearchPipeline(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        VectorSearch vectorSearch,
        FullTextSearch fullTextSearch,
        ContextExpander contextExpander,
        ILogger<SearchPipeline> logger) { ... }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        IReadOnlyList<string> collectionNames,
        SearchOptions options,
        CancellationToken ct = default) { ... }
}
```

**SearchAsync flow**:
1. Embed the query text using `IEmbeddingGenerator`
2. For each collection, run vector search and full-text search **in parallel** (`Task.WhenAll`)
3. Merge results via `RankFusion.Fuse` with `options.HybridAlpha`
4. Take top `options.TopK` results
5. If `options.ExpandContext`, run `ContextExpander.ExpandAsync`
6. Return `IReadOnlyList<SearchResult>`

For multi-collection search, fuse results per collection first, then merge across collections by final score.

### Step 6: Write unit tests

**File**: `tests/Minerva.Tests/Search/RankFusionTests.cs`
- Two disjoint ranked lists → correct merged order
- Overlapping ranked lists → shared items get boosted
- Alpha = 1.0 → vector-only order preserved
- Alpha = 0.0 → full-text-only order preserved
- Empty input lists handled gracefully

**File**: `tests/Minerva.Tests/Search/ContextExpanderTests.cs`
- Fetches prev/next chunks in a single batch call
- Populates ContextBefore/ContextAfter on results
- Handles edge chunks (no prev or no next) gracefully
- Handles empty result list

**File**: `tests/Minerva.Tests/Search/SearchPipelineTests.cs`
- Vector and full-text search run in parallel (mock both, verify both called)
- Results are fused and ordered by score
- Context expansion is skipped when `ExpandContext = false`
- Multi-collection search merges correctly

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `src/Minerva/Search/VectorSearch.cs` | pgvector cosine distance query |
| `src/Minerva/Search/FullTextSearch.cs` | PostgreSQL ts_rank full-text query |
| `src/Minerva/Search/RankFusion.cs` | Reciprocal Rank Fusion merge |
| `src/Minerva/Search/ContextExpander.cs` | Adjacent chunk fetching |
| `src/Minerva/Search/SearchPipeline.cs` | Search orchestrator |
| `src/Minerva/Search/RankedChunk.cs` | Internal record for ranked search results |
| `src/Minerva/Search/FusedResult.cs` | Internal record for fused search results |
| `tests/Minerva.Tests/Search/RankFusionTests.cs` | RRF unit tests |
| `tests/Minerva.Tests/Search/ContextExpanderTests.cs` | Context expansion tests |
| `tests/Minerva.Tests/Search/SearchPipelineTests.cs` | Pipeline orchestration tests |

---

## Verification Checklist

- [ ] RRF produces correct merged rank order for known inputs
- [ ] Alpha = 1.0 gives vector-only ordering; alpha = 0.0 gives FTS-only ordering
- [ ] ContextExpander fetches adjacent chunks in a single batch call
- [ ] SearchPipeline runs vector + FTS in parallel
- [ ] Multi-collection search merges results correctly
- [ ] Run: `dotnet test tests/Minerva.Tests --filter Category=Search`

⏸️ **GATE**: Sub-PRD complete. Continue to next sub-PRD or `/dev-checkpoint`.
