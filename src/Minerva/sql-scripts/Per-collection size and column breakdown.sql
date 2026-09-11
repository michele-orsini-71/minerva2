SELECT
    collection_name,
    COUNT(*)                                   AS chunks,
    pg_size_pretty(SUM(pg_column_size(content)))            AS content_bytes,
    pg_size_pretty(SUM(pg_column_size(embedding)))          AS embedding_bytes,
    pg_size_pretty(SUM(pg_column_size(c.*)))                AS row_total_bytes
FROM chunks c
GROUP BY collection_name
ORDER BY collection_name;
