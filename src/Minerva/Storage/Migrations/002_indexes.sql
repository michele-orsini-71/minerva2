-- B-tree indexes for lookups
CREATE INDEX IF NOT EXISTS idx_chunks_collection_source
    ON chunks(collection_name, source_id);
CREATE INDEX IF NOT EXISTS idx_chunks_source_id
    ON chunks(source_id);

-- Adjacency FK indexes
CREATE INDEX IF NOT EXISTS idx_chunks_prev ON chunks(prev_chunk_id);
CREATE INDEX IF NOT EXISTS idx_chunks_next ON chunks(next_chunk_id);
