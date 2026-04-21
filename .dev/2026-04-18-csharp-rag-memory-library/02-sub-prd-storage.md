# Sub-PRD: Storage Layer

**Parent**: [00-master-plan.md](./00-master-plan.md)
**Status**: Complete
**Dependency**: [01-sub-prd-scaffold.md](./01-sub-prd-scaffold.md)
**Last Updated**: 2026-04-07

---

## Implementation Progress

| Step | Description | Status |
|------|-------------|--------|
| **1** | Create repository interfaces | ✅ Complete |
| **2** | Create SQL migrations | ✅ Complete |
| **3** | Create SchemaInitializer | ✅ Complete |
| **4** | Implement PostgresCollectionRepository | ✅ Complete |
| **5** | Implement PostgresChunkRepository | ✅ Complete |
| **6** | Write integration tests | ✅ Complete |

---

## Goal

Implement all database access: schema creation via idempotent SQL migrations, collection CRUD, and chunk persistence with atomic upserts, adjacency FK columns (`prev_chunk_id`, `next_chunk_id`), pgvector embeddings, and tsvector full-text indexes. All using raw Npgsql — no Entity Framework.

---

## Implementation Steps

### Step 1: Create repository interfaces

**File**: `src/Minerva/Storage/ICollectionRepository.cs`

```csharp
public interface ICollectionRepository
{
    Task<Collection?> GetAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Collection>> ListAsync(CancellationToken ct = default);
    Task CreateAsync(Collection collection, CancellationToken ct = default);
    Task UpdateAsync(Collection collection, CancellationToken ct = default);
    Task DeleteAsync(string name, CancellationToken ct = default);
}
```

**File**: `src/Minerva/Storage/IChunkRepository.cs`

```csharp
public interface IChunkRepository
{
    // Atomic: delete all existing chunks for sourceId, insert new ones, in one transaction
    Task UpsertChunksAsync(string collectionName, string sourceId,
        IReadOnlyList<ChunkWithEmbedding> chunks, CancellationToken ct = default);

    Task DeleteBySourceIdAsync(string collectionName, string sourceId,
        CancellationToken ct = default);

    Task<string?> GetContentHashAsync(string collectionName, string sourceId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ChunkRecord>> GetAdjacentChunksAsync(
        IReadOnlyList<string> chunkIds, CancellationToken ct = default);

    // For vector search
    Task<IReadOnlyList<ChunkSearchRecord>> VectorSearchAsync(
        string collectionName, float[] queryEmbedding, int topK,
        CancellationToken ct = default);

    // For full-text search
    Task<IReadOnlyList<ChunkSearchRecord>> FullTextSearchAsync(
        string collectionName, string query, int topK,
        CancellationToken ct = default);
}
```

Note: `ChunkWithEmbedding`, `ChunkRecord`, and `ChunkSearchRecord` are internal storage records — define them alongside the interfaces or in `Models/`.

### Step 2: Create SQL migrations

**File**: `src/Minerva/Storage/Migrations/001_initial.sql`

```sql
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS collections (
    name TEXT PRIMARY KEY,
    description TEXT,
    embedding_model TEXT NOT NULL,
    embedding_dimension INT NOT NULL,
    metadata JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS chunks (
    id TEXT PRIMARY KEY,
    collection_name TEXT NOT NULL REFERENCES collections(name) ON DELETE CASCADE,
    source_id TEXT NOT NULL,
    chunk_index INT NOT NULL,
    content TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    contextual_prefix TEXT,
    embedding vector,  -- dimension set per collection, validated at insert time
    fts_vector TSVECTOR,
    metadata JSONB,
    prev_chunk_id TEXT REFERENCES chunks(id) ON DELETE SET NULL,
    next_chunk_id TEXT REFERENCES chunks(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

**File**: `src/Minerva/Storage/Migrations/002_indexes.sql`

```sql
-- B-tree indexes for lookups
CREATE INDEX IF NOT EXISTS idx_chunks_collection_source
    ON chunks(collection_name, source_id);
CREATE INDEX IF NOT EXISTS idx_chunks_source_id
    ON chunks(source_id);

-- GIN index for full-text search
CREATE INDEX IF NOT EXISTS idx_chunks_fts
    ON chunks USING GIN(fts_vector);

-- HNSW index for vector search (cosine distance)
-- Note: created per-collection after first insert, since dimension must be known
-- SchemaInitializer handles this dynamically

-- Adjacency FK indexes
CREATE INDEX IF NOT EXISTS idx_chunks_prev ON chunks(prev_chunk_id);
CREATE INDEX IF NOT EXISTS idx_chunks_next ON chunks(next_chunk_id);
```

Embed these as assembly resources in the `.csproj`:
```xml
<ItemGroup>
  <EmbeddedResource Include="Storage/Migrations/*.sql" />
</ItemGroup>
```

### Step 3: Create SchemaInitializer

**File**: `src/Minerva/Storage/SchemaInitializer.cs`

- Loads embedded SQL migration files in order (sorted by filename)
- Tracks applied migrations in a `_migrations` table
- Uses a PostgreSQL advisory lock to prevent concurrent migration runs
- Provides `CreateHnswIndexAsync(collectionName, dimension)` to create per-collection HNSW indexes after first insert
- Fully idempotent: safe to call on every app startup

```csharp
public class SchemaInitializer
{
    public SchemaInitializer(NpgsqlDataSource dataSource) { ... }
    public Task InitializeAsync(CancellationToken ct = default) { ... }
    public Task EnsureHnswIndexAsync(string collectionName, int dimension,
        CancellationToken ct = default) { ... }
}
```

### Step 4: Implement PostgresCollectionRepository

**File**: `src/Minerva/Storage/PostgresCollectionRepository.cs`

Raw Npgsql queries using `NpgsqlDataSource`:
- `GetAsync` — `SELECT` by name
- `ListAsync` — `SELECT` all
- `CreateAsync` — `INSERT`, serialize metadata to JSONB
- `UpdateAsync` — `UPDATE`, bump `last_updated_at`
- `DeleteAsync` — `DELETE` (cascades to chunks)

JSONB metadata serialized/deserialized via `System.Text.Json`.

### Step 5: Implement PostgresChunkRepository

**File**: `src/Minerva/Storage/PostgresChunkRepository.cs`

Key operations:

**UpsertChunksAsync**: Atomic in a single transaction:
1. `DELETE FROM chunks WHERE collection_name = $1 AND source_id = $2`
2. Batch `INSERT` new chunks with embeddings (using `NpgsqlBinaryImporter` for performance if batch is large)
3. `UPDATE` prev/next FK pointers for the newly inserted chunk chain
4. Generate `fts_vector` via `to_tsvector('english', $content)` — on the post-attachment text WITHOUT contextual prefix

**VectorSearchAsync**:
```sql
SELECT id, source_id, chunk_index, content, metadata,
       embedding <=> $1::vector AS distance
FROM chunks
WHERE collection_name = $2
ORDER BY embedding <=> $1::vector
LIMIT $3
```

**FullTextSearchAsync**:
```sql
SELECT id, source_id, chunk_index, content, metadata,
       ts_rank(fts_vector, plainto_tsquery('english', $1)) AS rank
FROM chunks
WHERE collection_name = $2 AND fts_vector @@ plainto_tsquery('english', $1)
ORDER BY rank DESC
LIMIT $3
```

**GetAdjacentChunksAsync**: Fetch by ID list using `ANY($1::text[])`.

**GetContentHashAsync**: Fetch `content_hash` from the first chunk (index 0) for a given sourceId, used for change detection.

### Step 6: Write integration tests

**File**: `tests/Minerva.IntegrationTests/Storage/StorageTestFixture.cs`

Test fixture that:
- Connects to a local PostgreSQL instance (connection string from env var or test config)
- Runs `SchemaInitializer.InitializeAsync()` once per test run
- Cleans up test data between tests

**File**: `tests/Minerva.IntegrationTests/Storage/CollectionRepositoryTests.cs`
- Create, get, list, update, delete a collection
- Verify JSONB metadata round-trips correctly

**File**: `tests/Minerva.IntegrationTests/Storage/ChunkRepositoryTests.cs`
- Insert chunks with embeddings, retrieve by sourceId
- Verify atomic upsert (old chunks deleted, new ones inserted)
- Verify adjacency FKs (prev/next chain)
- Verify content hash retrieval
- Verify vector search returns results ordered by distance
- Verify full-text search returns results matching query terms

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `src/Minerva/Storage/ICollectionRepository.cs` | Collection persistence interface |
| `src/Minerva/Storage/IChunkRepository.cs` | Chunk persistence interface |
| `src/Minerva/Storage/SchemaInitializer.cs` | Idempotent SQL migration runner |
| `src/Minerva/Storage/Migrations/001_initial.sql` | Tables: collections, chunks |
| `src/Minerva/Storage/Migrations/002_indexes.sql` | HNSW, GIN, B-tree indexes |
| `src/Minerva/Storage/PostgresCollectionRepository.cs` | Npgsql-backed collection CRUD |
| `src/Minerva/Storage/PostgresChunkRepository.cs` | Npgsql-backed chunk upsert + search |
| `tests/Minerva.IntegrationTests/Storage/StorageTestFixture.cs` | PostgreSQL test fixture |
| `tests/Minerva.IntegrationTests/Storage/CollectionRepositoryTests.cs` | Collection CRUD tests |
| `tests/Minerva.IntegrationTests/Storage/ChunkRepositoryTests.cs` | Chunk persistence + search tests |

---

## Verification Checklist

- [ ] `SchemaInitializer.InitializeAsync()` runs twice without errors (idempotent)
- [ ] Collection CRUD round-trips with JSONB metadata
- [ ] Chunk upsert atomically replaces old chunks
- [ ] Adjacency FK chain is correct (prev/next pointers)
- [ ] Vector search returns results ordered by cosine distance
- [ ] Full-text search returns results matching query terms
- [ ] `GetContentHashAsync` returns the stored hash for a known sourceId
- [ ] Run: `dotnet test tests/Minerva.IntegrationTests --filter Category=Storage`

⏸️ **GATE**: Sub-PRD complete. Continue to next sub-PRD or `/dev-checkpoint`.
