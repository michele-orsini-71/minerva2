DROP INDEX chunks_bm25_idx;
CREATE INDEX chunks_bm25_idx ON chunks
USING bm25 (
    id,
    ((coalesce(contextual_prefix || ' ', '') || content)::pdb.simple('ascii_folding=true', 'alias=search_text'))
) WITH (key_field = 'id');