-- Both extensions must already be installed by a superuser (see sql-scripts/README.md);
-- these statements only assert their presence.
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_search;

-- Provenance (embedding model, dimension, chunking invariants, last run) lives in metadata.
CREATE TABLE IF NOT EXISTS collections (
    name TEXT PRIMARY KEY,
    description TEXT,
    metadata JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Full text of each ingested source, as chunked (after attachment integration).
-- Chunks stay the unit of retrieval; this table serves whole-document reads.
-- content_hash is the hash of content, used by ingestion to skip unchanged sources.
CREATE TABLE IF NOT EXISTS sources (
    collection_name TEXT NOT NULL REFERENCES collections(name) ON DELETE CASCADE,
    source_id TEXT NOT NULL,
    title TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    content TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (collection_name, source_id)
);

-- A chunk never exists without its source row; deleting the source removes its chunks.
CREATE TABLE IF NOT EXISTS chunks (
    id TEXT PRIMARY KEY,
    collection_name TEXT NOT NULL REFERENCES collections(name) ON DELETE CASCADE,
    source_id TEXT NOT NULL,
    chunk_index INT NOT NULL,
    content TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    embedding vector,
    metadata JSONB,
    prev_chunk_id TEXT REFERENCES chunks(id) ON DELETE SET NULL,
    next_chunk_id TEXT REFERENCES chunks(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    FOREIGN KEY (collection_name, source_id)
        REFERENCES sources(collection_name, source_id) ON DELETE CASCADE
);

-- B-tree indexes for lookups
CREATE INDEX IF NOT EXISTS idx_chunks_collection_source
    ON chunks(collection_name, source_id);
CREATE INDEX IF NOT EXISTS idx_chunks_source_id
    ON chunks(source_id);

-- Adjacency FK indexes
CREATE INDEX IF NOT EXISTS idx_chunks_prev ON chunks(prev_chunk_id);
CREATE INDEX IF NOT EXISTS idx_chunks_next ON chunks(next_chunk_id);

-- Lexical leg: BM25 over the chunk text. The dense leg's HNSW indexes are
-- per collection and created at runtime by SchemaInitializer.EnsureHnswIndexAsync.
CREATE INDEX IF NOT EXISTS chunks_bm25_idx ON chunks
USING bm25 (
    id,
    (content::pdb.simple('ascii_folding=true'))
) WITH (key_field = 'id');
