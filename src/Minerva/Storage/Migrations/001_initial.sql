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
    embedding vector,
    metadata JSONB,
    prev_chunk_id TEXT REFERENCES chunks(id) ON DELETE SET NULL,
    next_chunk_id TEXT REFERENCES chunks(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
