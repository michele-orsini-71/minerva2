CREATE EXTENSION IF NOT EXISTS pg_search;
ALTER TABLE chunks DROP COLUMN fts_vector;
CREATE INDEX IF NOT EXISTS chunks_bm25_idx ON chunks
USING bm25 (
    id,
    (content::pdb.simple('ascii_folding=true'))
) WITH (key_field = 'id');