# Sub-PRD: Ingestion Pipeline

**Parent**: [00-master-plan.md](./00-master-plan.md)
**Status**: Complete
**Dependency**: [02-sub-prd-storage.md](./02-sub-prd-storage.md), [03-sub-prd-providers.md](./03-sub-prd-providers.md)
**Last Updated**: 2026-04-07

---

## Implementation Progress

| Step | Description | Status |
|------|-------------|--------|
| **1** | Create AttachmentIntegrator | ✅ Complete |
| **2** | Create DocumentChunker | ✅ Complete |
| **3** | Create EmbeddingService | ✅ Complete |
| **4** | Create DocumentSummarizer | ✅ Complete |
| **5** | Create ChunkContextualizer | ✅ Complete |
| **6** | Create IngestionPipeline | ✅ Complete |
| **7** | Write unit tests | ✅ Complete |

---

## Goal

Implement the full document ingestion path: attachment integration → optional summarization → chunking → optional contextualization → embedding → content-hash change detection → atomic storage. The pipeline must handle the three-way diff (added/updated/deleted/unchanged) for incremental updates.

---

## Implementation Steps

### Step 1: Create AttachmentIntegrator

**File**: `src/Minerva/Ingestion/AttachmentIntegrator.cs`

Replaces attachment syntax in document text with descriptions from the attachment dictionary.

```csharp
public static class AttachmentIntegrator
{
    // Replaces each key found in text with: key + "\n" + description
    // Preserves original text structure; descriptions are appended inline
    public static string Integrate(string text,
        Dictionary<string, AttachmentDescription>? attachments) { ... }
}
```

Example: if text contains `![[photo.jpg]]` and attachments has key `![[photo.jpg]]` with description "A sunset over mountains", the output contains both the original syntax and the description.

### Step 2: Create DocumentChunker

**File**: `src/Minerva/Ingestion/DocumentChunker.cs`

Two-stage markdown-aware chunking:

1. **Header split**: Use Markdig to parse markdown and split at heading boundaries (`#`, `##`, `###`). Each section becomes a candidate chunk.
2. **Size split**: For sections exceeding `ChunkingOptions.TargetChunkSize`, apply `TextChunker.SplitPlainTextParagraphs` from `Microsoft.SemanticKernel.Text` with configured overlap.

For large documents exceeding `ChunkingOptions.LargeDocumentThreshold`:
- Split into **segments** at highest-level headings that keep segments under the threshold
- Each segment is processed independently (summarized separately, chunked separately)
- Chunks track which segment they belong to

Output: ordered list of `Chunk` records with deterministic IDs via `HashHelper.GenerateChunkId`.

```csharp
public class DocumentChunker
{
    public DocumentChunker(ChunkingOptions options) { ... }

    public IReadOnlyList<Chunk> Chunk(
        string collectionName, string sourceId, string text) { ... }
}
```

### Step 3: Create EmbeddingService

**File**: `src/Minerva/Ingestion/EmbeddingService.cs`

Wraps `IEmbeddingGenerator` with batch-then-individual fallback (porting v1's pattern):

1. Group chunks into batches of `ProviderOptions.BatchSize`
2. Attempt to embed each batch
3. On batch failure, fall back to embedding each chunk individually with retry
4. Reports progress via `IProgress<int>` (count of embedded chunks)

```csharp
public class EmbeddingService
{
    public EmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        int batchSize) { ... }

    public Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        IProgress<int>? progress = null,
        CancellationToken ct = default) { ... }
}
```

### Step 4: Create DocumentSummarizer

**File**: `src/Minerva/Ingestion/DocumentSummarizer.cs`

Optional (controlled by `ChunkingOptions.EnableSummarization`).

Uses `IChatClient` to generate a one-paragraph summary of the document (or each segment for large documents). The summary is used as context for chunk contextualization in Step 5.

```csharp
public class DocumentSummarizer
{
    public DocumentSummarizer(IChatClient chatClient) { ... }

    // Returns null if summarization is disabled
    // For large docs, returns one summary per segment
    public Task<string?> SummarizeAsync(
        string text, CancellationToken ct = default) { ... }

    public Task<IReadOnlyList<string>> SummarizeSegmentsAsync(
        IReadOnlyList<string> segments, CancellationToken ct = default) { ... }
}
```

System prompt for summarization:
```
Summarize the following document in one paragraph. Focus on the main topics,
key concepts, and structure. This summary will be used to provide context
when embedding individual chunks of this document.
```

### Step 5: Create ChunkContextualizer

**File**: `src/Minerva/Ingestion/ChunkContextualizer.cs`

Optional (controlled by `ChunkingOptions.EnableContextualization`). Requires summarization to also be enabled (they are always skipped together per the PRD).

For each chunk, sends `summary + chunk` to the LLM and gets back a short contextual prefix (1-2 sentences) that situates the chunk within the document. This prefix is prepended to the chunk text before embedding but NOT included in the full-text search index.

```csharp
public class ChunkContextualizer
{
    public ChunkContextualizer(IChatClient chatClient) { ... }

    public Task<IReadOnlyList<string>> ContextualizeAsync(
        string documentSummary,
        IReadOnlyList<Chunk> chunks,
        CancellationToken ct = default) { ... }
}
```

System prompt (per Anthropic article):
```
<document>
{{WHOLE_DOCUMENT_SUMMARY}}
</document>
Here is the chunk we want to situate within the whole document:
<chunk>
{{CHUNK_CONTENT}}
</chunk>
Please give a short succinct context to situate this chunk within the overall
document for the purposes of improving search retrieval of the chunk.
Answer only with the succinct context and nothing else.
```

### Step 6: Create IngestionPipeline

**File**: `src/Minerva/Ingestion/IngestionPipeline.cs`

Orchestrates the full flow for a single document:

```csharp
public class IngestionPipeline
{
    public IngestionPipeline(
        DocumentChunker chunker,
        EmbeddingService embeddingService,
        DocumentSummarizer? summarizer,
        ChunkContextualizer? contextualizer,
        IChunkRepository chunkRepository,
        ILogger<IngestionPipeline> logger) { ... }

    public Task<IngestionResult> IngestAsync(
        string collectionName,
        Document document,
        CancellationToken ct = default) { ... }

    public Task RemoveAsync(
        string collectionName,
        string sourceId,
        CancellationToken ct = default) { ... }
}
```

**IngestAsync flow**:
1. Check content hash: `GetContentHashAsync(collectionName, sourceId)` — if unchanged, return `IngestionResult(Unchanged: 1)`
2. Integrate attachments into text
3. (Optional) Summarize document/segments
4. Chunk the text
5. (Optional) Contextualize each chunk (sets `ContextualPrefix`)
6. Compute text for embedding: `contextualPrefix + chunkContent` (or just `chunkContent` if no prefix)
7. Embed all chunks via `EmbeddingService`
8. Build `ChunkWithEmbedding` records with adjacency pointers (prev/next within the chunk list)
9. Atomic upsert via `IChunkRepository.UpsertChunksAsync`
10. Return `IngestionResult` with counts

**Error handling**: If any step fails (LLM timeout, embedding error), the old document version stays intact in the database. The method throws with details; the client can retry.

### Step 7: Write unit tests

**File**: `tests/Minerva.Tests/Ingestion/AttachmentIntegratorTests.cs`
- Replaces known syntax with descriptions
- Handles null/empty attachment dictionary (returns text unchanged)
- Handles missing keys gracefully (text unchanged for unmatched syntax)

**File**: `tests/Minerva.Tests/Ingestion/DocumentChunkerTests.cs`
- Splits multi-header markdown into expected chunks
- Respects chunk size limits
- Generates deterministic chunk IDs
- Handles large documents (segment splitting)
- Handles documents with no headers (falls back to size-only splitting)

**File**: `tests/Minerva.Tests/Ingestion/EmbeddingServiceTests.cs`
- Batches correctly per batch size
- Falls back to individual on batch failure
- Reports progress accurately
- Propagates cancellation

**File**: `tests/Minerva.Tests/Ingestion/IngestionPipelineTests.cs`
- Skips re-ingestion when content hash matches (unchanged)
- Returns correct added/updated counts
- Skips summarization/contextualization when disabled
- Chunk adjacency pointers are correct
- Throws on embedding failure (old data preserved)

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `src/Minerva/Ingestion/AttachmentIntegrator.cs` | Integrates attachment descriptions into text |
| `src/Minerva/Ingestion/DocumentChunker.cs` | Header-first markdown chunking |
| `src/Minerva/Ingestion/EmbeddingService.cs` | Batch + individual fallback embedding |
| `src/Minerva/Ingestion/DocumentSummarizer.cs` | Optional LLM document summarization |
| `src/Minerva/Ingestion/ChunkContextualizer.cs` | Optional per-chunk LLM context prefix |
| `src/Minerva/Ingestion/IngestionPipeline.cs` | Full ingestion orchestrator |
| `tests/Minerva.Tests/Ingestion/AttachmentIntegratorTests.cs` | Attachment integration tests |
| `tests/Minerva.Tests/Ingestion/DocumentChunkerTests.cs` | Chunking tests |
| `tests/Minerva.Tests/Ingestion/EmbeddingServiceTests.cs` | Embedding service tests |
| `tests/Minerva.Tests/Ingestion/IngestionPipelineTests.cs` | Pipeline orchestration tests |

---

## Verification Checklist

- [ ] AttachmentIntegrator replaces known syntax with descriptions
- [ ] DocumentChunker splits markdown correctly at header boundaries
- [ ] DocumentChunker handles large documents with segment splitting
- [ ] EmbeddingService batches and falls back correctly
- [ ] IngestionPipeline skips unchanged documents (content hash match)
- [ ] IngestionPipeline skips summarization/contextualization when disabled
- [ ] Chunk adjacency pointers form a correct linked list
- [ ] Run: `dotnet test tests/Minerva.Tests --filter Category=Ingestion`

⏸️ **GATE**: Sub-PRD complete. Continue to next sub-PRD or `/dev-checkpoint`.
