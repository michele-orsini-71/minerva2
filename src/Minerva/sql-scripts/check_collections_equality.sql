SELECT collection_name, COUNT(*) AS chunks, COUNT(DISTINCT source_id) AS docs
FROM chunks
WHERE collection_name IN (:'a', :'b')
GROUP BY collection_name;

SELECT source_id, chunk_index, content_hash
FROM chunks WHERE collection_name = :'a'
EXCEPT
SELECT source_id, chunk_index, content_hash
FROM chunks WHERE collection_name = :'b';
