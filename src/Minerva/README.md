# Minerva

Core library. Provides embedding, storage, and retrieval backed by PostgreSQL
with the `pgvector` and `pg_search` extensions.

The library is **client-agnostic**: it has no knowledge of Obsidian, Claude,
markdown, or any specific data source. Clients (see `Minerva.MarkdownIndexer`)
feed it `Document` objects through `IIngestEngine` and query it through
`ISearchEngine`.

## What it does

- **Ingestion**: chunk each document, embed each chunk, and store chunks,
  vectors and metadata in one transaction per document. A document whose
  content hash is unchanged is skipped; a changed document has all its chunks
  replaced; documents no longer supplied by the client are removed.
- **Hybrid search**: dense vector similarity + BM25 keyword search, merged
  via Reciprocal Rank Fusion (`HybridAlpha` weights the two legs). An optional
  cross-encoder reranker rescores the top `RerankDepth` fused candidates, and
  an optional cascade reranker rescores the top `CascadeDepth` of that list.
  If the reranker fails, search falls back to the fused ranking.
- **Collections**: each collection is bound to one embedding model and vector
  dimension, and records its build configuration (provenance). Ingesting with a
  different configuration is refused unless recreation is explicitly allowed.
- **Source access**: fetch the full text of a source, its metadata, or a window
  of chunks around a search hit.

## Public API

Two engines, each created by a builder that binds and validates its options,
checks the database and model endpoints, and initializes the schema:

```csharp
IIngestEngine ingest = await MinervaIngestBuilder.CreateAsync(config.GetSection("Minerva"), loggerFactory, ct);
ISearchEngine search = await MinervaSearchBuilder.CreateAsync(config, loggerFactory, ct);
```
See IIngestEngine.cs, ISearchEngine.cs and Models/ for the full API.

Ingestion syncs the collection with the full set of documents it receives,
so the client passes every document on each run. The collection is created on
first use.

Search has overrides for the `Search` configuration section (see below), they
 are all optional; a field left `null` uses the configured value.

## Configuration

Each search call can override any field of the `Search` section (see below);
a field left `null` uses the configured value.

Ingestion reads the `Minerva` section:

```json
{
  "Minerva": {
    "ConnectionString": "Host=localhost;Database=minerva;Username=minerva;Password=...",
    "Embedding": {
      "BaseUrl": "http://localhost:9930/v1",
      "Model": "text-embedding-bge-m3",
      "Concurrency": 1,
      "BatchSize": 1
    },
    "Chunking": {
      "TargetChunkSize": 1200,
      "ChunkOverlap": 200,
      "ChunkerType": "Custom"
    }
  }
}
```

Search reads the `Minerva` section (without `Chunking`, with the rerankers) and
the `Search` section:

```json
{
  "Minerva": {
    "ConnectionString": "Host=localhost;Database=minerva;Username=minerva;Password=...",
    "Embedding": { "BaseUrl": "http://localhost:9930/v1", "Model": "text-embedding-bge-m3", "Concurrency": 1, "BatchSize": 1 },
    "Reranker": { "BaseUrl": "http://localhost:9930", "Model": "bge-reranker" },
    "CascadeReranker": { "BaseUrl": "http://localhost:9930", "Model": "qwen3-reranker-4b" }
  },
  "Search": {
    "TopK": 10,
    "HybridAlpha": 0.7,
    "CandidatePoolSize": 100,
    "ExpandContext": false,
    "EnableReranker": true,
    "RerankDepth": 100,
    "CascadeDepth": 20
  }
}
```

| Field | Meaning |
| --- | --- |
| `Embedding.ApiKey` | optional; inline or `env:VAR_NAME` (resolved by `CredentialResolver`) |
| `Embedding.RequestsPerMinute` | optional; unlimited when absent |
| `Embedding.Concurrency`, `BatchSize` | parallel requests and chunks per request; `1` for local servers |
| `Chunking.ChunkOverlap` | characters shared by adjacent chunks; must be `< TargetChunkSize` |
| `Chunking.ChunkerType` | `Custom` (markdown-aware) or `SemanticKernel` |
| `Reranker`, `CascadeReranker` | optional; omit to disable that stage; `CascadeReranker` requires `Reranker` |
| `Search.HybridAlpha` | weight of the dense leg in the fusion, `0..1` |
| `Search.CandidatePoolSize` | candidates taken from each leg before fusion |
| `Search.ExpandContext` | attach the neighbouring chunks to each result |
| `Search.RerankDepth` | optional; fused candidates sent to the reranker; all when absent |
| `Search.CascadeDepth` | optional; top reranked results sent to the cascade reranker; `0` or absent skips it |

Embedding and reranker endpoints are any OpenAI-compatible server
(`/v1/embeddings`) and any server with a `/rerank` endpoint, e.g. llama.cpp.

## Internal layout

The folders follow the Clean Architecture rings described in
[`tests/Minerva.ArchitectureTests/README.md`](../../tests/Minerva.ArchitectureTests/README.md).

| Folder | Responsibility |
| --- | --- |
| (root) | `IIngestEngine`, `ISearchEngine`, their builders and implementations |
| `Collections/` | `CollectionManager` and the collection ports |
| `Configuration/` | Options records and their binders |
| `Exceptions/` | Typed exceptions |
| `Ingestion/` | `IngestionPipeline`, chunkers, `EmbeddingService`, attachment integration |
| `Models/` | Public records (`Document`, `SearchResult`, options, provenance, …) |
| `Providers/` | OpenAI-compatible embedding client, HTTP reranker client, `RateLimiter` |
| `Search/` | `SearchPipeline`, `VectorSearch`, `FullTextSearch`, `RankFusion`, `Reranker`, `ContextExpander` |
| `Storage/` | Postgres repositories, `SchemaInitializer`, migrations, `DatabasePreflight` |
| `Utilities/` | Hash helpers |
| `sql-scripts/` | Bootstrap and diagnostic SQL — see its README |
